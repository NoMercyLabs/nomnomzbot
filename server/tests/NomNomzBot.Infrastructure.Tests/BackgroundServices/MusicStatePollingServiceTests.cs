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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Music;

namespace NomNomzBot.Infrastructure.Tests.BackgroundServices;

/// <summary>
/// Proves E4's poller leg: <see cref="MusicStatePollingService"/> publishes <see cref="PlaybackStateChangedEvent"/>
/// only when a channel's playback state actually changed (track/play-flip/seek-drift), never on a repeat
/// observation of the same state, and that one channel's failure never stops the rest of the tick or crashes the
/// loop — with a capped backoff so a dead channel is not hammered every tick.
/// </summary>
public sealed class MusicStatePollingServiceTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000f1001");
    private static readonly Guid ChannelB = Guid.Parse("0192a000-0000-7000-8000-0000000f1002");

    /// <summary>The 2026-08-25 outage, as a test. A Spotify call hit HttpClient's 100s timeout, which
    /// raises TaskCanceledException — a subclass of OperationCanceledException — so the poller's
    /// `ex is not OperationCanceledException` filters did NOT catch it. It escaped the per-channel catch,
    /// aborted the sweep for every other channel, escaped ExecuteAsync, and with
    /// BackgroundServiceExceptionBehavior.StopHost took the entire bot down (5 crash-loop restarts,
    /// 502 dashboard). Both filters now key on the cancellation TOKEN instead of the exception type.</summary>
    [Fact]
    public async Task A_provider_timeout_is_survived_and_never_aborts_the_other_channels()
    {
        (
            MusicStatePollingService sut,
            RecordingEventBus bus,
            FakeMusicService music,
            FakeTimeProvider _,
            RecordingHandover _
        ) = Build([ChannelA, ChannelB]);
        music.SetThrowsTimeout(ChannelA);
        music.SetResponse(ChannelB, NowPlayingState("Song B", isPlaying: true, progressMs: 1_000));

        // Must not throw: before the fix this propagated straight out and killed the host.
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        music.Calls.Should().Contain(ChannelB, "one channel timing out must not abort the sweep");
        bus.Published.OfType<PlaybackStateChangedEvent>()
            .Should()
            .HaveCount(
                1,
                "the healthy channel still publishes its state despite the other timing out"
            );
    }

    [Fact]
    public async Task Same_state_observed_twice_publishes_only_once()
    {
        (
            MusicStatePollingService sut,
            RecordingEventBus bus,
            FakeMusicService music,
            FakeTimeProvider _,
            RecordingHandover _
        ) = Build([ChannelA]);
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 1_000));

        await sut.PollAllChannelsOnceAsync(CancellationToken.None);
        await sut.PollAllChannelsOnceAsync(CancellationToken.None); // clock unchanged — identical observation.

        bus.Published.OfType<PlaybackStateChangedEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task Track_change_publishes_the_new_track()
    {
        (MusicStatePollingService sut, RecordingEventBus bus, FakeMusicService music, _, _) =
            Build([ChannelA]);
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 1_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        music.SetResponse(ChannelA, NowPlayingState("Song B", isPlaying: true, progressMs: 500));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        List<PlaybackStateChangedEvent> published =
        [
            .. bus.Published.OfType<PlaybackStateChangedEvent>(),
        ];
        published.Should().HaveCount(2);
        published[1].TrackName.Should().Be("Song B");
    }

    [Fact]
    public async Task Pause_flip_publishes_the_new_play_state()
    {
        (MusicStatePollingService sut, RecordingEventBus bus, FakeMusicService music, _, _) =
            Build([ChannelA]);
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 1_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: false, progressMs: 1_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        List<PlaybackStateChangedEvent> published =
        [
            .. bus.Published.OfType<PlaybackStateChangedEvent>(),
        ];
        published.Should().HaveCount(2);
        published[1].IsPlaying.Should().BeFalse();
        published[1].TrackName.Should().Be("Song A");
    }

    [Fact]
    public async Task Seek_jump_beyond_drift_tolerance_publishes_again()
    {
        (
            MusicStatePollingService sut,
            RecordingEventBus bus,
            FakeMusicService music,
            FakeTimeProvider clock,
            _
        ) = Build([ChannelA]);
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 1_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        // 10s of real time passes (matching the poll cadence); a genuine seek jumps far past the ~11,000ms
        // the natural-progression math would expect, well beyond the drift tolerance.
        clock.Advance(TimeSpan.FromSeconds(10));
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 90_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        bus.Published.OfType<PlaybackStateChangedEvent>().Should().HaveCount(2);
    }

    [Fact]
    public async Task Natural_progression_within_tolerance_does_not_publish_again()
    {
        (
            MusicStatePollingService sut,
            RecordingEventBus bus,
            FakeMusicService music,
            FakeTimeProvider clock,
            _
        ) = Build([ChannelA]);
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 1_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        // 10s of real time passes and progress advances by exactly 10s — ordinary continuous playback, not a
        // seek. Must NOT be treated as a change (rail: publish ONLY on actual state change).
        clock.Advance(TimeSpan.FromSeconds(10));
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 11_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        bus.Published.OfType<PlaybackStateChangedEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task Volume_only_change_publishes_too()
    {
        (MusicStatePollingService sut, RecordingEventBus bus, FakeMusicService music, _, _) =
            Build([ChannelA]);
        music.SetResponse(
            ChannelA,
            NowPlayingState("Song A", isPlaying: true, progressMs: 1_000, volumePercent: 80)
        );
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        // Nothing about track/play-state/seek changed — only the device volume did (streamer's phone,
        // hardware knob, another app). This must still publish so a Stream Deck mute key can reflect it
        // within one poll tick instead of waiting on its own separate fallback resync.
        music.SetResponse(
            ChannelA,
            NowPlayingState("Song A", isPlaying: true, progressMs: 1_000, volumePercent: 35)
        );
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        List<PlaybackStateChangedEvent> published =
        [
            .. bus.Published.OfType<PlaybackStateChangedEvent>(),
        ];
        published.Should().HaveCount(2);
        published[1].VolumePercent.Should().Be(35);
    }

    [Fact]
    public async Task Permission_flip_with_nothing_else_changed_publishes_too()
    {
        (MusicStatePollingService sut, RecordingEventBus bus, FakeMusicService music, _, _) =
            Build([ChannelA]);
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 1_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        // An ad break starts mid-track: Spotify now disallows skipping next. Track/play-state/volume/seek
        // are all otherwise identical — this must still publish so a Stream Deck skip key dims in real time
        // instead of only failing silently on the next press.
        music.SetResponse(
            ChannelA,
            NowPlayingState("Song A", isPlaying: true, progressMs: 1_000, canSkipNext: false)
        );
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        List<PlaybackStateChangedEvent> published =
        [
            .. bus.Published.OfType<PlaybackStateChangedEvent>(),
        ];
        published.Should().HaveCount(2);
        published[1].CanSkipNext.Should().BeFalse();
    }

    [Fact]
    public async Task One_channel_throwing_does_not_stop_the_others_in_the_same_tick()
    {
        (MusicStatePollingService sut, RecordingEventBus bus, FakeMusicService music, _, _) =
            Build([ChannelA, ChannelB]);
        music.SetThrows(ChannelA);
        music.SetResponse(ChannelB, NowPlayingState("Song B", isPlaying: true, progressMs: 0));

        Func<Task> act = () => sut.PollAllChannelsOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        PlaybackStateChangedEvent published = bus
            .Published.OfType<PlaybackStateChangedEvent>()
            .Single();
        published.BroadcasterId.Should().Be(ChannelB);
    }

    [Fact]
    public async Task Failing_channel_backs_off_then_retries_after_the_window_elapses()
    {
        (
            MusicStatePollingService sut,
            RecordingEventBus _,
            FakeMusicService music,
            FakeTimeProvider clock,
            RecordingHandover _
        ) = Build([ChannelA]);
        music.SetThrows(ChannelA);

        await sut.PollAllChannelsOnceAsync(CancellationToken.None); // attempt 1 — fails, starts 30s backoff.
        music.Calls.Should().HaveCount(1);

        // Still inside the backoff window — skipped without a second attempt.
        clock.Advance(TimeSpan.FromSeconds(5));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);
        music.Calls.Should().HaveCount(1);

        // Past the 30s window — eligible again.
        clock.Advance(TimeSpan.FromSeconds(26));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);
        music.Calls.Should().HaveCount(2);
    }

    /// <summary>
    /// The song-request queue advances off playback CHANGES, but a handover that could not land — the
    /// streamer's player was closed, the token had died — leaves requests waiting with nothing at the
    /// provider, and a paused or unchanging player publishes no further change to wake the reconciler.
    /// The poller therefore asks on EVERY tick, including a tick that publishes nothing, so the queue
    /// resumes by itself rather than waiting for a viewer to request another song.
    /// </summary>
    [Fact]
    public async Task Every_tick_asks_to_un_stick_the_song_request_queue_even_when_nothing_changed()
    {
        (
            MusicStatePollingService sut,
            RecordingEventBus bus,
            FakeMusicService music,
            FakeTimeProvider _,
            RecordingHandover handover
        ) = Build([ChannelA, ChannelB]);
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 1_000));
        music.SetResponse(ChannelB, NowPlayingState("Song B", isPlaying: true, progressMs: 1_000));

        await sut.PollAllChannelsOnceAsync(CancellationToken.None);
        int publishedAfterFirstTick = bus.Published.Count;
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        // The second tick observes identical state and publishes nothing — and must still ask, for BOTH
        // channels: which channels were asked is the distinction, not how many calls were made.
        bus.Published.Count.Should()
            .Be(publishedAfterFirstTick, "unchanged state publishes nothing");
        // Order-insensitive on purpose: the candidate-channel query is a Distinct() with no ORDER BY,
        // so no real database promises an order. The multiset is the contract - every connected channel
        // asked exactly once per tick, twice over two ticks.
        handover
            .Calls.Should()
            .BeEquivalentTo(
                new[]
                {
                    ChannelA.ToString(),
                    ChannelB.ToString(),
                    ChannelA.ToString(),
                    ChannelB.ToString(),
                }
            );
    }

    private static (
        MusicStatePollingService Sut,
        RecordingEventBus Bus,
        FakeMusicService MusicService,
        FakeTimeProvider Clock,
        RecordingHandover Handover
    ) Build(IReadOnlyList<Guid> connectedChannels)
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        foreach (Guid channelId in connectedChannels)
        {
            db.Services.Add(
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "spotify",
                    BroadcasterId = channelId,
                    Enabled = true,
                    AccessToken = "test-access-token",
                }
            );
        }
        db.SaveChanges();

        RecordingEventBus bus = new();
        FakeMusicService music = new();
        FakeTimeProvider clock = new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        PollerScopeFactory scopes = new(db, music);
        MusicStatePollingService sut = new(
            scopes,
            bus,
            clock,
            NullLogger<MusicStatePollingService>.Instance
        );

        return (sut, bus, music, clock, scopes.Handover);
    }

    private static NowPlaying NowPlayingState(
        string trackName,
        bool isPlaying,
        int progressMs,
        int volumePercent = 100,
        bool canSkipNext = true
    ) =>
        new(
            trackName,
            "Artist",
            "Album",
            null,
            200_000,
            progressMs,
            isPlaying,
            volumePercent,
            null,
            "spotify",
            CanSkipNext: canSkipNext
        );

    /// <summary>A scope factory whose every scope resolves the shared test <see cref="IApplicationDbContext"/>,
    /// <see cref="IMusicService"/>, the registered <see cref="IMusicProvider"/> set, and the
    /// <see cref="ISongRequestHandover"/> the poller asks on each tick to un-stick a song-request queue whose
    /// earlier handover could not land (the provider set supplies the integration names whose connections
    /// count as "music-connected").</summary>
    private sealed class PollerScopeFactory(
        IApplicationDbContext db,
        IMusicService musicService,
        RecordingHandover? handover = null
    ) : IServiceScopeFactory
    {
        public RecordingHandover Handover { get; } = handover ?? new RecordingHandover();

        public IServiceScope CreateScope() => new Scope(db, musicService, Handover);

        private sealed class Scope(
            IApplicationDbContext db,
            IMusicService musicService,
            ISongRequestHandover handover
        ) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IApplicationDbContext))
                    return db;
                if (serviceType == typeof(IMusicService))
                    return musicService;
                if (serviceType == typeof(ISongRequestHandover))
                    return handover;
                if (serviceType == typeof(IEnumerable<IMusicProvider>))
                    return new List<IMusicProvider> { new RegisteredSpotifyStub() };
                return null;
            }

            public void Dispose() { }
        }
    }

    /// <summary>Records the channels the poller asked to un-stick, instead of touching a provider.</summary>
    internal sealed class RecordingHandover : ISongRequestHandover
    {
        public List<string> Calls { get; } = [];

        public Task HandOverNextAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(broadcasterId);
            return Task.CompletedTask;
        }
    }

    /// <summary>Registration stub matching the tests' seeded Service(Name="spotify") rows. The poller only
    /// reads <see cref="IMusicProvider.Provider"/>; every other member is unreachable from it.</summary>
    private sealed class RegisteredSpotifyStub : IMusicProvider
    {
        public string Provider => "spotify";

        public MusicProviderCapabilities Capabilities =>
            MusicProviderCapabilities.NowPlaying | MusicProviderCapabilities.PlaybackControl;

        public Task PlayAsync(Guid broadcasterId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PauseAsync(Guid broadcasterId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SkipAsync(Guid broadcasterId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PreviousAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetVolumeAsync(
            Guid broadcasterId,
            int volumePercent,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SeekAsync(
            Guid broadcasterId,
            int positionSeconds,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetShuffleAsync(
            Guid broadcasterId,
            bool enabled,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetRepeatAsync(
            Guid broadcasterId,
            MusicRepeatMode mode,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<MusicDeviceInfo>> GetDevicesAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task TransferPlaybackAsync(
            Guid broadcasterId,
            string deviceId,
            bool play,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<TrackInfo?> GetCurrentTrackAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<string?> GetEmbeddedPlaybackTokenAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<(
            IReadOnlyList<TrackInfo> Tracks,
            MusicProviderFailureReason Failure
        )> SearchAsync(
            Guid broadcasterId,
            string query,
            int maxResults = 5,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<(TrackInfo? Track, MusicProviderFailureReason Failure)> ResolveTrackAsync(
            Guid broadcasterId,
            string uriOrId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<bool> AddToQueueAsync(
            Guid broadcasterId,
            string trackUri,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    /// <summary>Hand-rolled <see cref="IMusicService"/> test double. Only <see cref="GetNowPlayingAsync"/> is
    /// reachable by the poller; every other member throws since the poller never calls it.</summary>
    private sealed class FakeMusicService : IMusicService
    {
        private readonly Dictionary<Guid, NowPlaying?> _responses = new();
        private readonly HashSet<Guid> _throwing = [];
        private readonly HashSet<Guid> _timingOut = [];

        public List<Guid> Calls { get; } = [];

        public void SetResponse(Guid broadcasterId, NowPlaying? nowPlaying) =>
            _responses[broadcasterId] = nowPlaying;

        public void SetThrows(Guid broadcasterId) => _throwing.Add(broadcasterId);

        /// <summary>Reproduces an HttpClient TIMEOUT, which surfaces as TaskCanceledException — a
        /// subclass of OperationCanceledException, and therefore the exact shape that escaped the
        /// old `ex is not OperationCanceledException` filter and stopped the whole host.</summary>
        public void SetThrowsTimeout(Guid broadcasterId) => _timingOut.Add(broadcasterId);

        public Task<NowPlaying?> GetNowPlayingAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        )
        {
            Guid channelId = Guid.Parse(broadcasterId);
            Calls.Add(channelId);

            if (_throwing.Contains(channelId))
                throw new InvalidOperationException($"Simulated provider failure for {channelId}.");

            if (_timingOut.Contains(channelId))
                throw new TaskCanceledException(
                    "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."
                );

            return Task.FromResult(
                _responses.TryGetValue(channelId, out NowPlaying? np) ? np : null
            );
        }

        public Task<IReadOnlyList<MusicTrack>> SearchAsync(
            string broadcasterId,
            string query,
            int maxResults = 5,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> PlayAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> PauseAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> SkipAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> PreviousAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<MusicQueue> GetQueueAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> AddToQueueAsync(
            string broadcasterId,
            string trackUri,
            string? requestedBy = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<MusicTrack>> RequestTrackAsync(
            string broadcasterId,
            string query,
            string? requestedBy = null,
            int? requesterRoleLevel = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<bool> PromoteToTopAsync(
            string broadcasterId,
            int position,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<BlockedTrackDto>> BanQueuedTrackAsync(
            string broadcasterId,
            int position,
            string? blockedByUserId = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<string?> GetActiveProviderKeyAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<string?> GetActiveProviderAuthStatusAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> SetVolumeAsync(
            string broadcasterId,
            int volume,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<bool> RemoveFromQueueAsync(
            string broadcasterId,
            int position,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> SeekAsync(
            string broadcasterId,
            int positionMs,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> SetShuffleAsync(
            string broadcasterId,
            bool enabled,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> SetRepeatAsync(
            string broadcasterId,
            string mode,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> TransferPlaybackAsync(
            string broadcasterId,
            string deviceId,
            bool play = false,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<MusicDeviceDto>> GetDevicesAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<MusicPlaylistDto>> GetPlaylistsAsync(
            string broadcasterId,
            int offset = 0,
            int limit = 20,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<bool> PlayContextAsync(
            string broadcasterId,
            string contextUri,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<string>> GetEmbeddedPlaybackTokenAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
