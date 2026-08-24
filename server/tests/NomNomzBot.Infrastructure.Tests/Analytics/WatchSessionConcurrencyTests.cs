// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves S004d: <c>WatchSessionProjection.ApplyAsync</c> no longer read-modify-writes a stale
/// <c>ViewerProfile.TotalWatchSeconds</c> (Analytics/WatchSessionProjection.cs:97, before the fix). Before the
/// fix, every concurrent fold loaded the SAME in-memory <c>profile</c>, added its own delta, and called
/// SaveChangesAsync — whichever writer committed second silently overwrote the other's delta (a lost update,
/// not a thrown exception, since <see cref="ViewerProfile"/> carries no unique index the two writers could
/// collide on). Real-world trigger: <c>EventStoreController.Replay</c>/<c>RebuildProjections</c> racing the
/// driver's own tick over the SAME broadcaster+viewer.
///
/// Each task below folds a DIFFERENT, pre-seeded, per-task <see cref="WatchSession"/> (distinct StreamId) for
/// the SAME viewer — isolating the race this slice fixes (concurrent accumulation into ONE shared
/// ViewerProfile row) from the separate, unfixed gap that <see cref="WatchSession"/> itself carries no unique
/// (BroadcasterId, ViewerUserId, StreamId) index (see OUT-OF-SCOPE FOUND), which would otherwise make the
/// session side of the arithmetic racy too and the assertion unsound.
/// </summary>
public sealed class WatchSessionConcurrencyTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
    private const string ViewerExternalId = "shared-watcher";
    private static readonly DateTime BaseTime = new(2026, 6, 22, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Concurrent_folds_across_independent_sessions_accumulate_the_shared_profile_total_without_loss()
    {
        string dbName = $"watchsession-race-{Guid.NewGuid():N}";
        const int concurrency = 15;

        // Prime the viewer identity/profile resolution ONCE up front, and pre-seed one WatchSession per task
        // on its OWN StreamId, so every concurrent task below finds an EXISTING session with no insert race —
        // isolating the ViewerProfile.TotalWatchSeconds accumulation this slice fixes.
        Guid viewerUserId;
        using (AuthDbContext seed = AuthTestBuilder.NewContext(dbName))
        {
            (ViewerResolver resolver, _) = BuildResolver(seed);
            ViewerProfile? primed = await resolver.ResolveAsync(
                Channel,
                "twitch",
                ViewerExternalId,
                "watcher",
                "Watcher",
                CancellationToken.None
            );
            primed.Should().NotBeNull();
            await seed.SaveChangesAsync();
            viewerUserId = primed.ViewerUserId;

            for (int i = 1; i <= concurrency; i++)
            {
                seed.WatchSessions.Add(
                    new()
                    {
                        BroadcasterId = Channel,
                        ViewerProfileId = primed.Id,
                        ViewerUserId = viewerUserId,
                        StreamId = $"stream-{i}",
                        StartedAt = BaseTime,
                        EndedAt = BaseTime,
                        CreatedAt = BaseTime,
                    }
                );
            }
            await seed.SaveChangesAsync();
        }

        // Task i extends its OWN session by exactly i*10 seconds — a single writer per session, so this delta
        // is deterministic regardless of interleaving. The sum below is the ONLY correct total if every
        // concurrent fold survives into the shared ViewerProfile row.
        Task[] tasks =
        [
            .. Enumerable
                .Range(1, concurrency)
                .Select(i =>
                    Task.Run(async () =>
                    {
                        using AuthDbContext db = AuthTestBuilder.NewContext(dbName);
                        (ViewerResolver resolver, IUserService _) = BuildResolver(db);
                        ILiveWindowResolver live = Substitute.For<ILiveWindowResolver>();
                        live.GetCoveringStreamIdAsync(
                                Arg.Any<Guid>(),
                                Arg.Any<DateTime>(),
                                Arg.Any<CancellationToken>()
                            )
                            .Returns($"stream-{i}");
                        WatchSessionProjection sut = new(db, resolver, live);

                        Result result = await sut.ApplyAsync(
                            ChatEvent(BaseTime.AddSeconds(i * 10))
                        );
                        result.IsSuccess.Should().BeTrue();
                    })
                ),
        ];

        await Task.WhenAll(tasks);

        long expectedTotal = Enumerable.Range(1, concurrency).Sum(i => (long)i * 10);

        using AuthDbContext verify = AuthTestBuilder.NewContext(dbName);
        ViewerProfile profile = verify
            .ViewerProfiles.IgnoreQueryFilters()
            .Single(p => p.BroadcasterId == Channel && p.ViewerUserId == viewerUserId);

        profile
            .TotalWatchSeconds.Should()
            .Be(
                expectedTotal,
                "a lost update would leave TotalWatchSeconds short of the sum of every concurrently-folded session delta"
            );
    }

    /// <summary>
    /// Proves S004f: <see cref="WatchSessionProjection"/>'s private <c>GetOrOpenAsync</c> no longer mints a
    /// duplicate <see cref="WatchSession"/> row when N concurrent folds race to open the SAME
    /// (BroadcasterId, ViewerUserId, StreamId) session — before the fix, <c>WatchSessionConfiguration</c>
    /// carried no unique index on that triple, so every racing insert committed independently. Every task
    /// here uses the IDENTICAL <c>OccurredAt</c> so the session's <c>DurationSeconds</c> delta is 0 for all
    /// of them (isolating the INSERT race this slice fixes from the separate, already-atomic
    /// ViewerProfile.TotalWatchSeconds fold and the unrelated session-duration read-modify-write).
    /// </summary>
    [Fact]
    public async Task Concurrent_GetOrOpenAsync_calls_for_the_same_key_mint_exactly_one_session_row()
    {
        string dbName = $"watchsession-open-race-{Guid.NewGuid():N}";
        const int concurrency = 20;
        const string streamId = "shared-open-race-stream";

        using (AuthDbContext seed = AuthTestBuilder.NewContext(dbName))
        {
            (ViewerResolver resolver, _) = BuildResolver(seed);
            ViewerProfile? primed = await resolver.ResolveAsync(
                Channel,
                "twitch",
                ViewerExternalId,
                "watcher",
                "Watcher",
                CancellationToken.None
            );
            primed.Should().NotBeNull();
            await seed.SaveChangesAsync();
        }

        Task[] tasks =
        [
            .. Enumerable
                .Range(1, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        using AuthDbContext db = AuthTestBuilder.NewContext(dbName);
                        (ViewerResolver resolver, IUserService _) = BuildResolver(db);
                        ILiveWindowResolver live = Substitute.For<ILiveWindowResolver>();
                        live.GetCoveringStreamIdAsync(
                                Arg.Any<Guid>(),
                                Arg.Any<DateTime>(),
                                Arg.Any<CancellationToken>()
                            )
                            .Returns(streamId);
                        WatchSessionProjection sut = new(db, resolver, live);

                        Result result = await sut.ApplyAsync(ChatEvent(BaseTime));
                        result.IsSuccess.Should().BeTrue();
                    })
                ),
        ];

        await Task.WhenAll(tasks);

        using AuthDbContext verify = AuthTestBuilder.NewContext(dbName);
        List<WatchSession> rows =
        [
            .. verify
                .WatchSessions.IgnoreQueryFilters()
                .Where(s => s.BroadcasterId == Channel && s.StreamId == streamId),
        ];

        rows.Should()
            .HaveCount(
                1,
                "the unique index on (BroadcasterId, ViewerUserId, StreamId) plus GetOrOpenAsync's "
                    + "insert-conflict fallback must converge every racing caller on ONE session row"
            );
    }

    /// <summary>
    /// Guards the fix's insert-conflict fallback against a regression on the NORMAL (no-conflict) path: N
    /// concurrent first-activity folds for N DIFFERENT streams (distinct keys, no pre-seed — unlike
    /// <see cref="Concurrent_folds_across_independent_sessions_accumulate_the_shared_profile_total_without_loss"/>,
    /// which pre-seeds so its concurrency is isolated to the ViewerProfile total fold) must each open exactly
    /// ONE session of its OWN, at its OWN correct StartedAt, with none of GetOrOpenAsync's new "insert failed,
    /// re-read the winner" fallback path ever triggering (there is no conflict to trigger it) and none of the
    /// N sessions merging into another's row.
    /// </summary>
    [Fact]
    public async Task Concurrent_opens_across_distinct_stream_keys_each_mint_their_own_correct_session()
    {
        string dbName = $"watchsession-distinct-open-{Guid.NewGuid():N}";
        const int concurrency = 15;

        Guid viewerUserId;
        using (AuthDbContext seed = AuthTestBuilder.NewContext(dbName))
        {
            (ViewerResolver resolver, IUserService _) = BuildResolver(seed);
            ViewerProfile? primed = await resolver.ResolveAsync(
                Channel,
                "twitch",
                ViewerExternalId,
                "watcher",
                "Watcher",
                CancellationToken.None
            );
            primed.Should().NotBeNull();
            await seed.SaveChangesAsync();
            viewerUserId = primed.ViewerUserId;
        }

        // No pre-seeding: every task's FIRST activity for its OWN stream races GetOrOpenAsync's INSERT path
        // concurrently with every other task's insert for a DIFFERENT key — proving the unique index + the
        // insert-conflict fallback added in this slice never cross-wires unrelated keys.
        DateTime[] openedAt =
        [
            .. Enumerable.Range(1, concurrency).Select(i => BaseTime.AddSeconds(i * 10)),
        ];
        Task[] tasks =
        [
            .. Enumerable
                .Range(1, concurrency)
                .Select(i =>
                    Task.Run(async () =>
                    {
                        using AuthDbContext db = AuthTestBuilder.NewContext(dbName);
                        (ViewerResolver resolver, IUserService _) = BuildResolver(db);
                        ILiveWindowResolver live = Substitute.For<ILiveWindowResolver>();
                        live.GetCoveringStreamIdAsync(
                                Arg.Any<Guid>(),
                                Arg.Any<DateTime>(),
                                Arg.Any<CancellationToken>()
                            )
                            .Returns($"distinct-stream-{i}");
                        WatchSessionProjection sut = new(db, resolver, live);

                        Result result = await sut.ApplyAsync(ChatEvent(openedAt[i - 1]));
                        result.IsSuccess.Should().BeTrue();
                    })
                ),
        ];

        await Task.WhenAll(tasks);

        using AuthDbContext verify = AuthTestBuilder.NewContext(dbName);
        List<WatchSession> rows =
        [
            .. verify
                .WatchSessions.IgnoreQueryFilters()
                .Where(s => s.BroadcasterId == Channel && s.ViewerUserId == viewerUserId)
                .ToList()
                .OrderBy(s => int.Parse(s.StreamId!.Split('-')[^1])),
        ];

        rows.Should()
            .HaveCount(
                concurrency,
                "each task's key is DISTINCT — no conflict should ever collapse two of them into one row"
            );
        for (int i = 0; i < concurrency; i++)
        {
            WatchSession row = rows[i];
            row.StreamId.Should().Be($"distinct-stream-{i + 1}");
            // A freshly-opened session always starts at DurationSeconds 0 (StartedAt == EndedAt == its own
            // first activity) — proves this task's insert landed on ITS OWN row, not a stray fallback re-read
            // of some OTHER task's winning insert.
            row.DurationSeconds.Should().Be(0);
        }

        // ViewerProfile accumulation must stay 0 too — every fold above is a fresh open (0 delta by
        // construction), so any non-zero total here would mean a fold mistakenly extended a PRE-EXISTING row
        // instead of opening its own.
        ViewerProfile profile = verify
            .ViewerProfiles.IgnoreQueryFilters()
            .Single(p => p.BroadcasterId == Channel && p.ViewerUserId == viewerUserId);
        profile.TotalWatchSeconds.Should().Be(0);
    }

    /// <summary>
    /// Proves S004h: <c>WatchSessionProjection.ApplyAsync</c> no longer extends an already-OPEN session's
    /// <c>EndedAt</c>/<c>DurationSeconds</c> via a plain tracked read-modify-write. Before the fix, every
    /// concurrent fold of the SAME open session re-derived its own "elapsed since StartedAt" as its
    /// DurationSeconds and folded the (new - old) delta into <see cref="ViewerProfile.TotalWatchSeconds"/> —
    /// two folds that both read the row before either committed both counted (their own copy of) the
    /// overlapping window, over-accumulating far past the real wall-clock bound. Pre-fix, running this exact
    /// scenario (15 concurrent folds, offsets 10s..150s of one open session) landed
    /// <c>ViewerProfile.TotalWatchSeconds</c> at 1200 (Σ 10+20+...+150) against the true 150s bound — the
    /// CAS-guarded <c>ExecuteUpdateAsync</c> retry loop this slice adds must converge on exactly 150.
    /// </summary>
    [Fact]
    public async Task Concurrent_folds_of_one_already_open_session_accumulate_exactly_the_real_elapsed_time()
    {
        string dbName = $"watchsession-open-extend-race-{Guid.NewGuid():N}";
        const int concurrency = 15;
        const string streamId = "shared-open-session";
        const long realBoundSeconds = concurrency * 10; // last task's offset — the true elapsed bound

        Guid viewerUserId;
        long sessionId;
        using (AuthDbContext seed = AuthTestBuilder.NewContext(dbName))
        {
            (ViewerResolver resolver, _) = BuildResolver(seed);
            ViewerProfile? primed = await resolver.ResolveAsync(
                Channel,
                "twitch",
                ViewerExternalId,
                "watcher",
                "Watcher",
                CancellationToken.None
            );
            primed.Should().NotBeNull();
            viewerUserId = primed.ViewerUserId;

            // ONE already-open session — every concurrent task below extends THIS SAME row, isolating the
            // extend-race this slice fixes from the separate (already-fixed) insert race S004f closed.
            WatchSession session = new()
            {
                BroadcasterId = Channel,
                ViewerProfileId = primed.Id,
                ViewerUserId = viewerUserId,
                StreamId = streamId,
                StartedAt = BaseTime,
                EndedAt = BaseTime,
                CreatedAt = BaseTime,
            };
            seed.WatchSessions.Add(session);
            await seed.SaveChangesAsync();
            sessionId = session.Id;
        }

        Task[] tasks =
        [
            .. Enumerable
                .Range(1, concurrency)
                .Select(i =>
                    Task.Run(async () =>
                    {
                        using AuthDbContext db = AuthTestBuilder.NewContext(dbName);
                        (ViewerResolver resolver, IUserService _) = BuildResolver(db);
                        ILiveWindowResolver live = Substitute.For<ILiveWindowResolver>();
                        live.GetCoveringStreamIdAsync(
                                Arg.Any<Guid>(),
                                Arg.Any<DateTime>(),
                                Arg.Any<CancellationToken>()
                            )
                            .Returns(streamId);
                        WatchSessionProjection sut = new(db, resolver, live);

                        Result result = await sut.ApplyAsync(
                            ChatEvent(BaseTime.AddSeconds(i * 10))
                        );
                        result.IsSuccess.Should().BeTrue();
                    })
                ),
        ];

        await Task.WhenAll(tasks);

        using AuthDbContext verify = AuthTestBuilder.NewContext(dbName);
        WatchSession finalSession = verify
            .WatchSessions.IgnoreQueryFilters()
            .Single(s => s.Id == sessionId);
        finalSession
            .DurationSeconds.Should()
            .Be(
                realBoundSeconds,
                "the session's own DurationSeconds must converge on the true elapsed bound, not the sum "
                    + "of every concurrent fold's independently re-derived duration"
            );

        ViewerProfile profile = verify
            .ViewerProfiles.IgnoreQueryFilters()
            .Single(p => p.BroadcasterId == Channel && p.ViewerUserId == viewerUserId);
        profile
            .TotalWatchSeconds.Should()
            .Be(
                realBoundSeconds,
                "a lost-update-shaped race that instead OVER-counts would inflate this past the real bound "
                    + "(pre-fix this observed 1200s against the true 150s)"
            );
    }

    private static (ViewerResolver Resolver, IUserService UserService) BuildResolver(
        AuthDbContext db
    )
    {
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        IUserService userService = AuthTestBuilder.UserService(db, currentUser, scopeFactory);
        return (new(db, userService), userService);
    }

    private static EventRecord ChatEvent(DateTime at) =>
        new(
            Random.Shared.NextInt64(),
            Guid.NewGuid(),
            Channel,
            0,
            "ChatMessageReceivedEvent",
            1,
            "domain",
            $"{{\"UserId\":\"{ViewerExternalId}\",\"UserLogin\":\"{ViewerExternalId}\",\"UserDisplayName\":\"{ViewerExternalId}\"}}",
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            "{}",
            at,
            at
        );
}
