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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The Spotify poll goes to the Spotify engine through <see cref="IEngineFeed"/>; the engine turns it into
/// reports and track ends and the director decides. Runs the real <see cref="SpotifyPlaybackEngine"/> over a
/// substitute provider, so the track-end rule is the production one.
/// </summary>
public sealed class SpotifyPollFeedTests
{
    private const string First = "spotify:track:s1";
    private const string Second = "spotify:track:s2";

    private readonly SongRequestQueueStore _store = new();
    private readonly IMusicProvider _provider = Substitute.For<
        IMusicProvider,
        IMusicProviderStartTrack,
        IMusicProviderConnectionGate
    >();
    private const string Mine = "spotify:track:owner";
    private const string Video = "dQw4w9WgXcQ";
    private static readonly string VideoUri = $"https://www.youtube.com/watch?v={Video}";

    private readonly PolledEngineFactory _engines;
    private readonly PlaybackDirectorRegistry _registry;

    public SpotifyPollFeedTests()
    {
        ((IMusicProviderConnectionGate)_provider)
            .IsConnectedAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(true);
        StartTrack
            .StartTrackAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _engines = new(_provider);
        _registry = new(_store, _engines, new RecordingBus(), TimeProvider.System);
    }

    private IMusicProviderStartTrack StartTrack => (IMusicProviderStartTrack)_provider;

    private void Request(string uri, string owner = "alice") =>
        _store
            .GetOrCreate(Channel.ToString())
            .Enqueue(owner, new(uri, uri, "Artist", null, 200_000, owner, 0, null, uri));

    private List<string> Queued() =>
        [.. _store.GetOrCreate(Channel.ToString()).GetSnapshot().Select(e => e.Item.TrackUri)];

    private static TrackInfo Polled(
        string uri,
        int progressMs,
        bool playing = true,
        string? deviceId = null
    ) =>
        new()
        {
            TrackName = uri,
            Artist = "Artist",
            Album = "Album",
            TrackUri = uri,
            Provider = "spotify",
            DurationMs = 200_000,
            ProgressMs = progressMs,
            IsPlaying = playing,
            DeviceId = deviceId,
        };

    private int StartsOf(string uri) =>
        StartTrack
            .ReceivedCalls()
            .Count(call =>
                call.GetMethodInfo().Name == nameof(IMusicProviderStartTrack.StartTrackAsync)
                && (string?)call.GetArguments()[1] == uri
            );

    [Fact]
    public async Task Row28_a_poll_from_another_device_during_our_track_blocks_and_sends_no_command()
    {
        Request(First);
        Request(Second, "bob");
        await _registry.SendAsync(Channel, new RequestAdded());
        await _registry.ObservePollAsync(Channel, Polled(First, 5_000, deviceId: "desk-pc"));
        int startsBefore = StartsOf(First) + StartsOf(Second);

        await _registry.ObservePollAsync(Channel, Polled(First, 8_000, deviceId: "phone"));

        _registry
            .StateOf(Channel)
            .Should()
            .Be(PlaybackState.Blocked("Spotify moved to another device"));
        (StartsOf(First) + StartsOf(Second)).Should().Be(startsBefore);
        Queued().Should().Equal(Second);
    }

    [Fact]
    public async Task A_track_that_is_not_the_head_playing_removes_only_that_row()
    {
        Request(First);
        Request(Second, "bob");
        await _registry.SendAsync(Channel, new RequestAdded());

        await _registry.ObservePollAsync(Channel, Polled(Second, 5_000));

        Queued().Should().Equal(First);
        StartsOf(First).Should().Be(1);
    }

    [Fact]
    public async Task The_previous_track_gone_near_its_end_ends_once_and_starts_the_next_head()
    {
        Request(First);
        Request(Second, "bob");
        await _registry.SendAsync(Channel, new RequestAdded());
        await _registry.ObservePollAsync(Channel, Polled(First, 5_000));
        Queued().Should().Equal(Second);
        await _registry.ObservePollAsync(Channel, Polled(First, 195_000));

        await _registry.ObservePollAsync(Channel, null);
        await _registry.ObservePollAsync(Channel, null);

        StartsOf(Second).Should().Be(1, "one TrackEnded starts the next head once");
        StartsOf(First).Should().Be(1);
        // The head leaves only when its own playing report arrives.
        Queued().Should().Equal(Second);

        await _registry.ObservePollAsync(Channel, Polled(Second, 1_000));

        Queued().Should().BeEmpty();
    }

    [Fact]
    public async Task A_poll_for_a_channel_with_no_director_changes_nothing_and_does_not_throw()
    {
        Request(First);

        Func<Task> poll = () => _registry.ObservePollAsync(Channel, Polled(First, 1_000));

        await poll.Should().NotThrowAsync();
        _engines.CreateCount.Should().Be(0, "a poll never creates a director or its engines");
        Queued().Should().Equal(First);
        StartsOf(First).Should().Be(0);
    }

    [Fact]
    public async Task A_channel_whose_engines_cannot_be_polled_is_a_no_op()
    {
        PlaybackDirectorRegistry fakeOnly = new(
            _store,
            new FakeEngineFactory(),
            new RecordingBus(),
            TimeProvider.System
        );
        Request(First);
        await fakeOnly.SendAsync(Channel, new RequestAdded());

        Func<Task> poll = () => fakeOnly.ObservePollAsync(Channel, Polled(First, 1_000));

        await poll.Should().NotThrowAsync();
        Queued().Should().Equal(First);
    }

    private int PausesOfSpotify() =>
        _provider
            .ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(IMusicProvider.PauseAsync));

    [Fact]
    public async Task Row7_An_owner_pause_seen_in_the_poll_holds_the_youtube_head_across_ten_polls()
    {
        await _registry.SendAsync(Channel, new OutputAttached());
        await _registry.ObservePollAsync(Channel, Polled(Mine, 100_000));
        Request(VideoUri);
        await _registry.SendAsync(Channel, new RequestAdded());

        for (int poll = 0; poll < 11; poll++)
            await _registry.ObservePollAsync(Channel, Polled(Mine, 100_000, playing: false));

        _registry
            .StateOf(Channel)
            .Should()
            .Be(PlaybackState.PausedByOwner(EngineKind.Spotify, null));
        _engines.YouTube.Calls.Should().BeEmpty("the owner's pause holds the YouTube head");
        PausesOfSpotify().Should().Be(0, "the pause is the owner's, the bot sends none");
        Queued().Should().Equal(VideoUri);
    }

    [Fact]
    public async Task Row11_Spotify_starting_on_its_own_while_youtube_plays_is_paused_through_the_provider()
    {
        Request(VideoUri);
        await _registry.SendAsync(Channel, new RequestAdded());
        await _engines.YouTube.EmitAsync(
            new EngineReport(EngineKind.YouTube, VideoUri, true, 1_000, 200_000)
        );
        _registry.StateOf(Channel).Kind.Should().Be(PlaybackStateKind.Playing);

        await _registry.ObservePollAsync(Channel, Polled(Mine, 1_000));

        PausesOfSpotify().Should().Be(1, "the foreign engine is silenced once");
        _registry.StateOf(Channel).Engine.Should().Be(EngineKind.YouTube);
        _registry.StateOf(Channel).Kind.Should().Be(PlaybackStateKind.Playing);
        _engines.YouTube.Calls.Should().ContainSingle(call => call.Name == "Start");
    }

    /// <summary>Builds a real Spotify engine over the substitute provider and a fake YouTube engine.</summary>
    private sealed class PolledEngineFactory(IMusicProvider provider) : IPlaybackEngineFactory
    {
        public FakeEngine YouTube { get; } = new(EngineKind.YouTube);

        public int CreateCount { get; private set; }

        public IReadOnlyList<IPlaybackEngine> Create(Guid broadcasterId)
        {
            CreateCount++;

            return
            [
                new SpotifyPlaybackEngine(broadcasterId, () => EngineLease.Of(provider)),
                YouTube,
            ];
        }
    }
}
