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
            viewerUserId = primed!.ViewerUserId;

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
        Task[] tasks = Enumerable
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

                    Result result = await sut.ApplyAsync(ChatEvent(BaseTime.AddSeconds(i * 10)));
                    result.IsSuccess.Should().BeTrue();
                })
            )
            .ToArray();

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
