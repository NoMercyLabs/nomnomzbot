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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream.Jobs;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// Proves the live-status reconcile (<see cref="StreamStatusPollingService.ApplyStreamState"/>) that fixes the
/// "live channel shows offline on the dashboard" bug: the poll writes the authoritative Helix Get Streams read into
/// both the in-memory <see cref="ChannelContext"/> (what the dashboard reads) and the persisted <see cref="Channel"/>,
/// treats an empty Helix result as offline, anchors the uptime clock only on the offline→live edge, captures the
/// live viewer count into the in-memory context (so the dashboard shows it from startup), and reports whether a
/// PERSISTED field changed so the caller saves exactly once per cycle — viewer count, being transient in-memory
/// state, never forces that save.
/// </summary>
public sealed class StreamStatusPollingServiceTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private static Result<TwitchStream> Live(string title, string game, int viewerCount = 7) =>
        Result.Success(
            new TwitchStream(
                Id: "stream-1",
                UserId: "42",
                UserLogin: "aaoa_",
                UserName: "aaoa_",
                GameId: "509658",
                GameName: game,
                Type: "live",
                Title: title,
                Tags: [],
                ViewerCount: viewerCount,
                StartedAt: StartedAt,
                Language: "en",
                ThumbnailUrl: "https://cdn/thumb",
                IsMature: false
            )
        );

    // Genuinely offline = Helix returned an EMPTY data[] → the transport surfaces it as a NotFound failure.
    private static Result<TwitchStream> Offline() =>
        Result.Failure<TwitchStream>("Twitch returned no data.", TwitchErrorCodes.NotFound);

    // A BROKEN read (rate-limit / 401 / 5xx / transport) — a failure that is NOT the empty-data NotFound. This is
    // NOT an offline signal and must never downgrade a live channel.
    private static Result<TwitchStream> Inconclusive() =>
        Result.Failure<TwitchStream>(
            "Twitch rate-limited the request.",
            TwitchErrorCodes.RateLimited
        );

    private static ChannelContext Ctx(bool isLive) =>
        new()
        {
            BroadcasterId = Guid.NewGuid(),
            TwitchChannelId = "42",
            ChannelName = "aaoa_",
            IsLive = isLive,
        };

    [Fact]
    public void Offline_to_live_sets_live_title_game_viewers_and_anchors_uptime()
    {
        ChannelContext ctx = Ctx(isLive: false);
        Channel dbChannel = new() { IsLive = false };

        bool changed = StreamStatusPollingService.ApplyStreamState(
            ctx,
            dbChannel,
            Live("Speedrun night", "Celeste", viewerCount: 152)
        );

        changed.Should().BeTrue("the persisted IsLive flag flipped offline→live");
        ctx.IsLive.Should().BeTrue();
        ctx.CurrentTitle.Should().Be("Speedrun night");
        ctx.CurrentGame.Should().Be("Celeste");
        ctx.WentLiveAt.Should().Be(StartedAt, "the uptime clock anchors on the rising edge");
        ctx.ViewerCount.Should().Be(152, "the live viewer count is captured for the dashboard");
        dbChannel.IsLive.Should().BeTrue();
        dbChannel.Title.Should().Be("Speedrun night");
        dbChannel.GameName.Should().Be("Celeste");
    }

    [Fact]
    public void Live_to_offline_clears_live_uptime_and_viewers()
    {
        ChannelContext ctx = Ctx(isLive: true);
        ctx.WentLiveAt = StartedAt;
        ctx.CurrentTitle = "Speedrun night";
        ctx.ViewerCount = 42;
        Channel dbChannel = new() { IsLive = true, Title = "Speedrun night" };

        bool changed = StreamStatusPollingService.ApplyStreamState(ctx, dbChannel, Offline());

        changed.Should().BeTrue("the persisted IsLive flag flipped live→offline");
        ctx.IsLive.Should().BeFalse();
        ctx.WentLiveAt.Should().BeNull("uptime is meaningless once offline");
        ctx.ViewerCount.Should().Be(0, "viewer count resets to 0 once offline");
        dbChannel.IsLive.Should().BeFalse();
    }

    [Fact]
    public void An_inconclusive_read_leaves_a_live_channel_live()
    {
        // THE recurring "a live channel shows offline" bug, proven: a transient Helix failure (rate-limit / 401 /
        // 5xx) is NOT a real offline, so ApplyStreamState must leave a LIVE channel exactly as EventSub set it and
        // persist nothing. Before the fix, `isLive = result.IsSuccess` flipped it offline on every failing poll —
        // every ~2 minutes — which is why the bug kept coming back.
        ChannelContext ctx = Ctx(isLive: true);
        ctx.WentLiveAt = StartedAt;
        ctx.CurrentTitle = "Speedrun night";
        ctx.ViewerCount = 42;
        Channel dbChannel = new() { IsLive = true, Title = "Speedrun night" };

        bool changed = StreamStatusPollingService.ApplyStreamState(ctx, dbChannel, Inconclusive());

        changed
            .Should()
            .BeFalse("an inconclusive read persists nothing — the caller must not save");
        ctx.IsLive.Should().BeTrue("a broken Helix read must never flip a live channel offline");
        dbChannel.IsLive.Should().BeTrue();
        ctx.WentLiveAt.Should()
            .Be(StartedAt, "the uptime anchor is preserved on an inconclusive read");
        ctx.ViewerCount.Should().Be(42, "the viewer count is untouched on an inconclusive read");
    }

    [Fact]
    public void Still_offline_is_a_no_op_change()
    {
        ChannelContext ctx = Ctx(isLive: false);
        Channel dbChannel = new() { IsLive = false };

        bool changed = StreamStatusPollingService.ApplyStreamState(ctx, dbChannel, Offline());

        changed.Should().BeFalse("nothing persisted changed — the caller must not save");
        ctx.IsLive.Should().BeFalse();
        dbChannel.IsLive.Should().BeFalse();
    }

    [Fact]
    public void Still_live_with_unchanged_metadata_reports_no_change_and_does_not_reset_uptime()
    {
        ChannelContext ctx = Ctx(isLive: true);
        ctx.WentLiveAt = StartedAt;
        ctx.ViewerCount = 7;
        Channel dbChannel = new()
        {
            IsLive = true,
            Title = "Speedrun night",
            GameName = "Celeste",
        };

        bool changed = StreamStatusPollingService.ApplyStreamState(
            ctx,
            dbChannel,
            Live("Speedrun night", "Celeste")
        );

        changed.Should().BeFalse("already live with the same title/game — no persisted change");
        ctx.IsLive.Should().BeTrue();
        ctx.WentLiveAt.Should()
            .Be(StartedAt, "uptime anchor is only set on the rising edge, not re-stamped");
    }

    [Fact]
    public void Title_change_while_already_live_is_a_persisted_change()
    {
        ChannelContext ctx = Ctx(isLive: true);
        Channel dbChannel = new()
        {
            IsLive = true,
            Title = "Old title",
            GameName = "Celeste",
        };

        bool changed = StreamStatusPollingService.ApplyStreamState(
            ctx,
            dbChannel,
            Live("New title", "Celeste")
        );

        changed.Should().BeTrue("the title changed, so the row must be saved");
        ctx.CurrentTitle.Should().Be("New title");
        dbChannel.Title.Should().Be("New title");
    }

    [Fact]
    public void Viewer_count_refreshes_in_memory_without_forcing_a_db_save()
    {
        ChannelContext ctx = Ctx(isLive: true);
        ctx.WentLiveAt = StartedAt;
        ctx.ViewerCount = 7;
        Channel dbChannel = new()
        {
            IsLive = true,
            Title = "Speedrun night",
            GameName = "Celeste",
        };

        bool changed = StreamStatusPollingService.ApplyStreamState(
            ctx,
            dbChannel,
            Live("Speedrun night", "Celeste", viewerCount: 152)
        );

        // Viewer count is in-memory only: the dashboard sees the new figure, but no persisted field moved, so the
        // caller must not write the row on every poll just because the count ticked.
        changed
            .Should()
            .BeFalse("only the transient viewer count moved — nothing persisted changed");
        ctx.ViewerCount.Should().Be(152);
    }

    [Fact]
    public void FoldPeakViewers_stamps_only_new_highs()
    {
        NomNomzBot.Domain.Stream.Entities.Stream row = new()
        {
            Id = "s-1",
            ChannelId = Guid.NewGuid(),
        };

        StreamStatusPollingService.FoldPeakViewers(row, 10).Should().BeTrue("first sample");
        row.PeakViewers.Should().Be(10);
        StreamStatusPollingService
            .FoldPeakViewers(row, 10)
            .Should()
            .BeFalse("an equal sample is not a new high — no write");
        StreamStatusPollingService.FoldPeakViewers(row, 7).Should().BeFalse("lower sample");
        row.PeakViewers.Should().Be(10);
        StreamStatusPollingService.FoldPeakViewers(row, 25).Should().BeTrue("new high");
        row.PeakViewers.Should().Be(25);
        StreamStatusPollingService
            .FoldPeakViewers(null, 99)
            .Should()
            .BeFalse("no stream row to stamp");
    }

    [Fact]
    public void A_live_poll_builds_a_journalable_viewer_count_sample()
    {
        Guid broadcaster = Guid.NewGuid();

        NomNomzBot.Domain.Stream.Events.StreamViewerCountSampledEvent? sample =
            StreamStatusPollingService.BuildViewerCountSample(
                broadcaster,
                Live("Speedrun night", "Celeste", viewerCount: 152)
            );

        sample.Should().NotBeNull("every live poll is a viewer-count fact for the analytics fold");
        sample.BroadcasterId.Should().Be(broadcaster);
        sample.ViewerCount.Should().Be(152);
        sample.StreamId.Should().Be("stream-1");
    }

    [Fact]
    public void An_offline_poll_builds_no_sample_so_the_journal_stays_quiet()
    {
        NomNomzBot.Domain.Stream.Events.StreamViewerCountSampledEvent? sample =
            StreamStatusPollingService.BuildViewerCountSample(Guid.NewGuid(), Offline());

        sample.Should().BeNull("no stream, no sample — offline polls must not journal noise");
    }

    /// <summary>
    /// The 2026-08-25 outage shape, reproduced at the poll-tick seam: a Helix Get Streams call can raise
    /// <see cref="TaskCanceledException"/> on its own timeout — a subclass of
    /// <see cref="OperationCanceledException"/> that an `ex is not OperationCanceledException` filter would
    /// let straight through, aborting the tick and (via BackgroundServiceExceptionBehavior.StopHost)
    /// taking the whole bot down. Both the per-channel and the outer tick catch must survive it, and the
    /// per-channel catch must keep polling every other channel in the same tick.
    /// </summary>
    [Fact]
    public async Task A_provider_timeout_for_one_channel_is_survived_and_the_other_channel_still_polls()
    {
        Guid timingOutChannel = Guid.Parse("0192f000-0000-7000-8000-0000000000a1");
        Guid healthyChannel = Guid.Parse("0192f000-0000-7000-8000-0000000000a2");
        Guid owner = Guid.Parse("0192f000-0000-7000-8000-0000000000a9");

        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = timingOutChannel,
                OwnerUserId = owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-1",
                TwitchChannelId = "tw-1",
                Name = "streamer1",
                NameNormalized = "streamer1",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.Channels.Add(
            new()
            {
                Id = healthyChannel,
                OwnerUserId = owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-2",
                TwitchChannelId = "tw-2",
                Name = "streamer2",
                NameNormalized = "streamer2",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.SaveChanges();

        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels
            .GetAll()
            .Returns(
                new List<ChannelContext>
                {
                    new()
                    {
                        BroadcasterId = timingOutChannel,
                        TwitchChannelId = "tw-1",
                        ChannelName = "streamer1",
                        IsLive = false,
                    },
                    new()
                    {
                        BroadcasterId = healthyChannel,
                        TwitchChannelId = "tw-2",
                        ChannelName = "streamer2",
                        IsLive = false,
                    },
                }
            );

        IPlatformBotReadinessGate gate = Substitute.For<IPlatformBotReadinessGate>();
        gate.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);

        ITwitchStreamsApi streams = Substitute.For<ITwitchStreamsApi>();
        streams
            .GetStreamAsync(timingOutChannel, Arg.Any<CancellationToken>())
            .Returns<Task<Result<TwitchStream>>>(_ =>
                throw new TaskCanceledException(
                    "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."
                )
            );
        streams.GetStreamAsync(healthyChannel, Arg.Any<CancellationToken>()).Returns(Offline());

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(gate)
            .AddSingleton(streams)
            .AddSingleton(Substitute.For<IEventBus>())
            .BuildServiceProvider();

        StreamStatusPollingService sut = new(
            channels,
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<StreamStatusPollingService>.Instance
        );

        Func<Task> act = () => sut.PollAllAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("a timeout on one channel must not abort the poll tick");
        await streams.Received(1).GetStreamAsync(healthyChannel, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The root cause of the 2026-08-27 watch-time inflation bug, reproduced and proven fixed: a channel goes
    /// offline WITHOUT its stream.offline EventSub ever arriving (dropped webhook / reconnect gap), so its
    /// Stream row is left with EndedAt == null. Before this fix that row stayed open forever and
    /// ILiveWindowResolver treated every later instant as still "inside" it. The poll must detect the
    /// live→offline edge itself and close the stale row as a backstop.
    /// </summary>
    [Fact]
    public async Task A_missed_offline_event_leaves_the_stream_open_until_the_poll_closes_it()
    {
        Guid broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000b1");
        Guid owner = Guid.Parse("0192f000-0000-7000-8000-0000000000b9");
        const string openStreamId = "stream-open-1";
        DateTimeOffset fixedNow = new(2026, 8, 27, 15, 0, 0, TimeSpan.Zero);

        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = broadcaster,
                OwnerUserId = owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-3",
                TwitchChannelId = "tw-3",
                Name = "streamer3",
                NameNormalized = "streamer3",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
                IsLive = true,
            }
        );
        db.Streams.Add(
            new()
            {
                Id = openStreamId,
                ChannelId = broadcaster,
                StartedAt = StartedAt,
                EndedAt = null, // the missed-offline symptom
            }
        );
        db.SaveChanges();

        ChannelContext ctx = new()
        {
            BroadcasterId = broadcaster,
            TwitchChannelId = "tw-3",
            ChannelName = "streamer3",
            IsLive = true,
            CurrentStreamId = openStreamId,
        };
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.GetAll().Returns(new List<ChannelContext> { ctx });

        IPlatformBotReadinessGate gate = Substitute.For<IPlatformBotReadinessGate>();
        gate.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);

        ITwitchStreamsApi streams = Substitute.For<ITwitchStreamsApi>();
        streams.GetStreamAsync(broadcaster, Arg.Any<CancellationToken>()).Returns(Offline());

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(gate)
            .AddSingleton(streams)
            .AddSingleton(Substitute.For<IEventBus>())
            .BuildServiceProvider();

        StreamStatusPollingService sut = new(
            channels,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(fixedNow),
            NullLogger<StreamStatusPollingService>.Instance
        );

        await sut.PollAllAsync(CancellationToken.None);

        NomNomzBot.Domain.Stream.Entities.Stream closed = await db.Streams.SingleAsync(s =>
            s.Id == openStreamId
        );
        closed.EndedAt.Should().Be(fixedNow, "the poll's backstop must close the stale-open row");
        ctx.IsLive.Should().BeFalse();
        ctx.CurrentStreamId.Should()
            .BeNull("the closed stream must not keep being polled as current");
    }
}
