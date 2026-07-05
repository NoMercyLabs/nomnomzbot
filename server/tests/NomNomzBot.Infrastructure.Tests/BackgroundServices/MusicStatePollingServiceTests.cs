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
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.BackgroundServices;
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

    [Fact]
    public async Task Same_state_observed_twice_publishes_only_once()
    {
        (
            MusicStatePollingService sut,
            RecordingEventBus bus,
            FakeMusicService music,
            FakeTimeProvider _
        ) = Build([ChannelA]);
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 1_000));

        await sut.PollAllChannelsOnceAsync(CancellationToken.None);
        await sut.PollAllChannelsOnceAsync(CancellationToken.None); // clock unchanged — identical observation.

        bus.Published.OfType<PlaybackStateChangedEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task Track_change_publishes_the_new_track()
    {
        (MusicStatePollingService sut, RecordingEventBus bus, FakeMusicService music, _) = Build([
            ChannelA,
        ]);
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 1_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        music.SetResponse(ChannelA, NowPlayingState("Song B", isPlaying: true, progressMs: 500));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        List<PlaybackStateChangedEvent> published = bus
            .Published.OfType<PlaybackStateChangedEvent>()
            .ToList();
        published.Should().HaveCount(2);
        published[1].TrackName.Should().Be("Song B");
    }

    [Fact]
    public async Task Pause_flip_publishes_the_new_play_state()
    {
        (MusicStatePollingService sut, RecordingEventBus bus, FakeMusicService music, _) = Build([
            ChannelA,
        ]);
        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: true, progressMs: 1_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        music.SetResponse(ChannelA, NowPlayingState("Song A", isPlaying: false, progressMs: 1_000));
        await sut.PollAllChannelsOnceAsync(CancellationToken.None);

        List<PlaybackStateChangedEvent> published = bus
            .Published.OfType<PlaybackStateChangedEvent>()
            .ToList();
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
            FakeTimeProvider clock
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
            FakeTimeProvider clock
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
    public async Task One_channel_throwing_does_not_stop_the_others_in_the_same_tick()
    {
        (MusicStatePollingService sut, RecordingEventBus bus, FakeMusicService music, _) = Build([
            ChannelA,
            ChannelB,
        ]);
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
            FakeTimeProvider clock
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

    private static (
        MusicStatePollingService Sut,
        RecordingEventBus Bus,
        FakeMusicService MusicService,
        FakeTimeProvider Clock
    ) Build(IReadOnlyList<Guid> connectedChannels)
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        foreach (Guid channelId in connectedChannels)
        {
            db.Services.Add(
                new Service
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
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        MusicStatePollingService sut = new(
            new PollerScopeFactory(db, music),
            bus,
            clock,
            NullLogger<MusicStatePollingService>.Instance
        );

        return (sut, bus, music, clock);
    }

    private static NowPlaying NowPlayingState(string trackName, bool isPlaying, int progressMs) =>
        new(
            trackName,
            "Artist",
            "Album",
            null,
            200_000,
            progressMs,
            isPlaying,
            100,
            null,
            "spotify"
        );

    /// <summary>A scope factory whose every scope resolves the shared test <see cref="IApplicationDbContext"/>,
    /// <see cref="IMusicService"/>, and the registered <see cref="IMusicProvider"/> set — the three dependencies
    /// <see cref="MusicStatePollingService"/> resolves per tick (the provider set supplies the integration
    /// names whose connections count as "music-connected").</summary>
    private sealed class PollerScopeFactory(IApplicationDbContext db, IMusicService musicService)
        : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(db, musicService);

        private sealed class Scope(IApplicationDbContext db, IMusicService musicService)
            : IServiceScope,
                IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IApplicationDbContext))
                    return db;
                if (serviceType == typeof(IMusicService))
                    return musicService;
                if (serviceType == typeof(IEnumerable<IMusicProvider>))
                    return new List<IMusicProvider> { new RegisteredSpotifyStub() };
                return null;
            }

            public void Dispose() { }
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

        public Task<IReadOnlyList<TrackInfo>> SearchAsync(
            Guid broadcasterId,
            string query,
            int maxResults = 5,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<TrackInfo?> ResolveTrackAsync(
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

        public List<Guid> Calls { get; } = [];

        public void SetResponse(Guid broadcasterId, NowPlaying? nowPlaying) =>
            _responses[broadcasterId] = nowPlaying;

        public void SetThrows(Guid broadcasterId) => _throwing.Add(broadcasterId);

        public Task<NowPlaying?> GetNowPlayingAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        )
        {
            Guid channelId = Guid.Parse(broadcasterId);
            Calls.Add(channelId);

            if (_throwing.Contains(channelId))
                throw new InvalidOperationException($"Simulated provider failure for {channelId}.");

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

        public Task<bool> AddToQueueAsync(
            string broadcasterId,
            string trackUri,
            string? requestedBy = null,
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
    }
}
