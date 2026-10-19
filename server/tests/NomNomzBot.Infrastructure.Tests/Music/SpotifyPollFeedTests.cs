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

    private static TrackInfo Polled(string uri, int progressMs, bool playing = true) =>
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
        };

    private int StartsOf(string uri) =>
        StartTrack
            .ReceivedCalls()
            .Count(call =>
                call.GetMethodInfo().Name == nameof(IMusicProviderStartTrack.StartTrackAsync)
                && (string?)call.GetArguments()[1] == uri
            );

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

    /// <summary>Builds a real Spotify engine over the substitute provider and a fake YouTube engine.</summary>
    private sealed class PolledEngineFactory(IMusicProvider provider) : IPlaybackEngineFactory
    {
        public int CreateCount { get; private set; }

        public IReadOnlyList<IPlaybackEngine> Create(Guid broadcasterId)
        {
            CreateCount++;

            return
            [
                new SpotifyPlaybackEngine(broadcasterId, () => EngineLease.Of(provider)),
                new FakeEngine(EngineKind.YouTube),
            ];
        }
    }
}
