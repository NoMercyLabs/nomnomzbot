// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A restart during a video must not leave Spotify paused for good (Stoney, 2026-11-09: Spotify must not
/// break while he streams). Each life is a new registry over the same real database.
/// </summary>
public sealed class PlaybackDirectorHeldSpotifyRestartTests : IDisposable
{
    private const string OwnTrack = "spotify:track:own";
    private const string OwnNextTrack = "spotify:track:own-next";
    private const string VideoUri = "youtube:video:made-up";

    private static readonly TimeSpan PastGrace = TimeSpan.FromSeconds(16);
    private static readonly EngineCall Resume = new("Resume");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly HeldSpotifyStore _held;
    private readonly FakeTimeProvider _clock = NewClock();

    public PlaybackDirectorHeldSpotifyRestartTests()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        using (IServiceScope scope = _services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        }

        _held = new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<HeldSpotifyStore>.Instance
        );
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private sealed class Life
    {
        private readonly SongRequestQueueStore _store = new();

        public Life(HeldSpotifyStore held, FakeTimeProvider clock, bool active = true)
        {
            IActiveInstanceGate gate = Substitute.For<IActiveInstanceGate>();
            gate.IsActiveInstance.Returns(active);
            Registry = new(_store, Engines, new RecordingBus(), clock, null, gate, null, held);
        }

        public FakeEngineFactory Engines { get; } = new();

        public PlaybackDirectorRegistry Registry { get; }

        public FakeEngine Spotify => Engines.EngineOf(Channel, EngineKind.Spotify);

        public FakeEngine YouTube => Engines.EngineOf(Channel, EngineKind.YouTube);

        public PlaybackState State => Registry.StateOf(Channel);

        public Task TickAsync() => Registry.SendAsync(Channel, new RequestAdded());

        public async Task OwnerPlaysSpotifyAsync()
        {
            await TickAsync();
            await Spotify.EmitAsync(Report(EngineKind.Spotify, OwnTrack, true));
        }

        /// <summary>A video has taken over from the owner's Spotify and reported playing.</summary>
        public async Task VideoHoldsSpotifyAsync()
        {
            await OwnerPlaysSpotifyAsync();
            QueueVideo();
            await TickAsync();
            await Spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, OwnTrack));
            await Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, true));
            await Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, false));
            await YouTube.EmitAsync(Report(EngineKind.YouTube, VideoUri, true));
            State.Engine.Should().Be(EngineKind.YouTube);
        }

        /// <summary>The owner pressed skip on his own Spotify music with a video at the head.</summary>
        public async Task OwnerSkipsToAVideoAsync()
        {
            await OwnerPlaysSpotifyAsync();
            QueueVideo();
            await Registry.SendAsync(Channel, new OwnerSkip());
            await Spotify.EmitAsync(Report(EngineKind.Spotify, OwnTrack, false));
            await YouTube.EmitAsync(Report(EngineKind.YouTube, VideoUri, true));
            State.Engine.Should().Be(EngineKind.YouTube);
        }

        /// <summary>The owner pressed previous on his own Spotify music, back to an earlier video.</summary>
        public async Task OwnerGoesBackToAVideoAsync()
        {
            await VideoHoldsSpotifyAsync();
            await YouTube.EmitAsync(new TrackEnded(EngineKind.YouTube, VideoUri));
            await Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, true));
            await Registry.SendAsync(Channel, new OwnerPrevious());
            await Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, false));
            await YouTube.EmitAsync(Report(EngineKind.YouTube, VideoUri, true));
            State.Engine.Should().Be(EngineKind.YouTube);
        }

        public void QueueVideo() =>
            _store
                .GetOrCreate(Channel.ToString())
                .Enqueue(
                    "viewer",
                    new SongRequestEntry(
                        VideoUri,
                        "Made up video",
                        "Artist",
                        null,
                        200_000,
                        "viewer",
                        0,
                        null,
                        ""
                    )
                );
    }

    private static EngineReport Report(EngineKind engine, string uri, bool playing) =>
        new(engine, uri, playing, 1_000, 200_000);

    private Life NewLife(bool active = true) => new(_held, _clock, active);

    private Task<DateTimeOffset?> HeldFactAsync() => _held.HeldSinceAsync(Channel);

    /// <summary>The old process is gone: its deadline timer no longer runs on the shared clock.</summary>
    private async Task ProcessDiesDuringAVideoAsync()
    {
        Life first = NewLife();
        await first.VideoHoldsSpotifyAsync();
        first.Registry.Dispose();
    }

    [Fact]
    public async Task A_takeover_writes_the_held_fact_while_the_video_plays()
    {
        Life first = NewLife();

        await first.VideoHoldsSpotifyAsync();

        (await HeldFactAsync()).Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task A_restart_with_no_live_video_resumes_Spotify_once_and_clears_the_fact()
    {
        await ProcessDiesDuringAVideoAsync();
        Life second = NewLife();

        await second.TickAsync();
        second.Spotify.Calls.Should().NotContain(Resume, "a page gets time to report first");

        _clock.Advance(PastGrace);
        await second.TickAsync();
        await second.TickAsync();

        second.Spotify.Calls.Should().Equal(Resume);
        second.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        await second.Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, true));
        (await HeldFactAsync()).Should().BeNull();
    }

    [Fact]
    public async Task The_cleared_fact_sends_no_second_Resume_on_the_next_restart()
    {
        await ProcessDiesDuringAVideoAsync();
        Life second = NewLife();
        await second.TickAsync();
        _clock.Advance(PastGrace);
        await second.TickAsync();
        second.Spotify.Calls.Should().Equal(Resume);
        await second.Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, true));

        Life third = NewLife();
        await third.TickAsync();
        _clock.Advance(PastGrace);
        await third.TickAsync();

        third.Spotify.Calls.Should().NotContain(Resume);
    }

    [Fact]
    public async Task A_page_still_playing_keeps_Spotify_held_until_the_video_ends()
    {
        await ProcessDiesDuringAVideoAsync();
        Life second = NewLife();

        await second.TickAsync();
        await second.YouTube.EmitAsync(Report(EngineKind.YouTube, VideoUri, true));
        _clock.Advance(TimeSpan.FromSeconds(10));
        await second.YouTube.EmitAsync(Report(EngineKind.YouTube, VideoUri, true));
        _clock.Advance(TimeSpan.FromSeconds(10));
        await second.YouTube.EmitAsync(Report(EngineKind.YouTube, VideoUri, true));
        await second.TickAsync();

        second.Spotify.Calls.Should().NotContain(Resume);
        second.State.Engine.Should().Be(EngineKind.YouTube);
        (await HeldFactAsync()).Should().NotBeNull();

        await second.YouTube.EmitAsync(new TrackEnded(EngineKind.YouTube, VideoUri));

        second.Spotify.Calls.Should().Equal(Resume);
        second.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        await second.Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, true));
        (await HeldFactAsync()).Should().BeNull();
    }

    [Fact]
    public async Task A_video_that_ends_normally_clears_the_fact_so_a_restart_sends_nothing()
    {
        Life first = NewLife();
        await first.VideoHoldsSpotifyAsync();

        await first.YouTube.EmitAsync(new TrackEnded(EngineKind.YouTube, VideoUri));

        first.Spotify.Calls.Should().ContainSingle(c => c == Resume);
        await first.Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, true));
        (await HeldFactAsync()).Should().BeNull();
        Life second = NewLife();
        await second.TickAsync();
        _clock.Advance(PastGrace);
        await second.TickAsync();
        second.Spotify.Calls.Should().NotContain(Resume);
    }

    [Fact]
    public async Task The_owners_own_pause_stores_no_fact_so_a_restart_sends_no_Resume()
    {
        Life first = NewLife();
        await first.OwnerPlaysSpotifyAsync();
        await first.Spotify.EmitAsync(Report(EngineKind.Spotify, OwnTrack, false));
        (await HeldFactAsync()).Should().BeNull();

        Life second = NewLife();
        await second.TickAsync();
        _clock.Advance(PastGrace);
        await second.TickAsync();

        second.Spotify.Calls.Should().BeEmpty();
        second.State.Kind.Should().Be(PlaybackStateKind.Idle);
    }

    [Fact]
    public async Task A_standby_instance_does_not_act_on_the_fact()
    {
        await _held.SaveAsync(Channel, _clock.GetUtcNow());
        Life standby = NewLife(active: false);

        await standby.TickAsync();
        await standby.Registry.SendAsync(Channel, Report(EngineKind.Spotify, OwnNextTrack, false));
        _clock.Advance(PastGrace);
        await standby.Registry.SendAsync(Channel, Report(EngineKind.Spotify, OwnNextTrack, false));

        standby.Spotify.Calls.Should().BeEmpty();
        (await HeldFactAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task A_fact_older_than_the_window_is_dropped_without_a_Resume()
    {
        await _held.SaveAsync(Channel, _clock.GetUtcNow());
        _clock.Advance(TimeSpan.FromHours(5));
        Life second = NewLife();

        await second.TickAsync();
        _clock.Advance(PastGrace);
        await second.TickAsync();

        second.Spotify.Calls.Should().BeEmpty();
        (await HeldFactAsync()).Should().BeNull();
    }

    /// <summary>The new process finds the fact, gives the page its time, then gives Spotify back once.</summary>
    private async Task RestartResumesSpotifyOnceAsync()
    {
        Life second = NewLife();

        await second.TickAsync();
        second.Spotify.Calls.Should().NotContain(Resume, "a page gets time to report first");

        _clock.Advance(PastGrace);
        await second.TickAsync();
        await second.TickAsync();

        second.Spotify.Calls.Should().Equal(Resume);
        second.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        await second.Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, true));
        (await HeldFactAsync()).Should().BeNull();
    }

    [Fact]
    public async Task A_restart_during_a_video_the_owner_skipped_to_gives_Spotify_back()
    {
        Life first = NewLife();
        await first.OwnerSkipsToAVideoAsync();
        (await HeldFactAsync()).Should().Be(_clock.GetUtcNow());
        first.Registry.Dispose();

        await RestartResumesSpotifyOnceAsync();
    }

    [Fact]
    public async Task A_restart_during_a_video_the_owner_went_back_to_gives_Spotify_back()
    {
        Life first = NewLife();
        await first.OwnerGoesBackToAVideoAsync();
        (await HeldFactAsync()).Should().Be(_clock.GetUtcNow());
        first.Registry.Dispose();

        await RestartResumesSpotifyOnceAsync();
    }

    [Fact]
    public async Task A_skipped_to_video_that_ends_clears_the_fact_so_a_restart_sends_nothing()
    {
        Life first = NewLife();
        await first.OwnerSkipsToAVideoAsync();
        (await HeldFactAsync()).Should().NotBeNull();

        await first.YouTube.EmitAsync(new TrackEnded(EngineKind.YouTube, VideoUri));

        first.Spotify.Calls.Should().ContainSingle(c => c == Resume);
        await first.Spotify.EmitAsync(Report(EngineKind.Spotify, OwnTrack, true));
        (await HeldFactAsync()).Should().BeNull();
        Life second = NewLife();
        await second.TickAsync();
        _clock.Advance(PastGrace);
        await second.TickAsync();
        second.Spotify.Calls.Should().NotContain(Resume);
    }

    [Fact]
    public async Task A_gone_back_to_video_that_ends_clears_the_fact_so_a_restart_sends_nothing()
    {
        Life first = NewLife();
        await first.OwnerGoesBackToAVideoAsync();
        (await HeldFactAsync()).Should().NotBeNull();

        await first.YouTube.EmitAsync(new TrackEnded(EngineKind.YouTube, VideoUri));

        first.Spotify.Calls.Count(c => c == Resume).Should().Be(2, "one return per video");
        await first.Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, true));
        (await HeldFactAsync()).Should().BeNull();
        Life second = NewLife();
        await second.TickAsync();
        _clock.Advance(PastGrace);
        await second.TickAsync();
        second.Spotify.Calls.Should().NotContain(Resume);
    }

    [Fact]
    public async Task A_skip_that_never_paused_Spotify_leaves_no_fact()
    {
        Life first = NewLife();
        await first.OwnerPlaysSpotifyAsync();
        first.YouTube.CanStart = false;
        first.QueueVideo();

        await first.Registry.SendAsync(Channel, new OwnerSkip());

        first.Spotify.Calls.Should().BeEmpty("Spotify plays on when the video cannot start");
        first.State.Engine.Should().Be(EngineKind.Spotify);
        (await HeldFactAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Spotify_already_playing_after_the_restart_gets_no_Resume()
    {
        await ProcessDiesDuringAVideoAsync();
        Life second = NewLife();
        await second.TickAsync();
        second.Spotify.ProbeReport = Report(EngineKind.Spotify, OwnNextTrack, true);

        _clock.Advance(PastGrace);
        await second.TickAsync();

        second.Spotify.Calls.Should().NotContain(Resume);
        (await HeldFactAsync()).Should().BeNull();
    }
}
