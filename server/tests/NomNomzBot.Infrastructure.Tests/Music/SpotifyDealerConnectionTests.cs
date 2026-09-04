// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Realtime;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves S-MUSIC-1's push leg: a <c>PLAYER_STATE_CHANGED</c> dealer frame publishes
/// <see cref="PlaybackStateChangedEvent"/> the instant it lands (not on the next poll tick), and a dropped
/// socket reconnects with a doubling backoff schedule and re-establishes the player-notification subscription
/// on every fresh connection — the same reconnect discipline <c>WebSocketEventSubTransport</c> already proves
/// for Twitch's EventSub socket.
/// </summary>
public sealed class SpotifyDealerConnectionTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f2001");

    private const string PlayerStateChangedFrame = """
        {"payloads":[{"events":[{"type":"PLAYER_STATE_CHANGED","event":{"state":{
            "is_playing":true,
            "progress_ms":12345,
            "shuffle_state":true,
            "repeat_state":"track",
            "item":{
                "id":"track123",
                "uri":"spotify:track:track123",
                "name":"Test Song",
                "duration_ms":200000,
                "artists":[{"id":"artist1","name":"Test Artist"}],
                "album":{"name":"Test Album","images":[{"url":"https://img.example/art.jpg"}]}
            },
            "device":{"volume_percent":55}
        }}}]}]}
        """;

    private static string ConnectionIdFrame(string connectionId) =>
        "{\"headers\":{\"Spotify-Connection-Id\":\"" + connectionId + "\"}}";

    [Fact]
    public async Task PlayerStateChangedFrame_PublishesPlaybackStateChangedEvent_ImmediatelyAndNudgesThePoller()
    {
        RecordingEventBus bus = new();
        MusicRealtimeSignal realtime = new();
        FakeTimeProvider clock = new(new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));
        SpotifyDealerConnection connection = NewConnection(bus, realtime, clock);

        bool handled = await connection.HandleFrameAsync(
            PlayerStateChangedFrame,
            CancellationToken.None
        );

        handled.Should().BeTrue("a PLAYER_STATE_CHANGED frame is a recognised frame shape");

        PlaybackStateChangedEvent published = bus
            .Published.OfType<PlaybackStateChangedEvent>()
            .Should()
            .ContainSingle("the frame lands off-cycle, not on a poll tick")
            .Subject;

        published.BroadcasterId.Should().Be(ChannelId);
        published.Provider.Should().Be("spotify");
        published.IsPlaying.Should().BeTrue();
        published.TrackName.Should().Be("Test Song");
        published.Artist.Should().Be("Test Artist");
        published.Album.Should().Be("Test Album");
        published.AlbumArtUrl.Should().Be("https://img.example/art.jpg");
        published.TrackUri.Should().Be("spotify:track:track123");
        published.ArtistId.Should().Be("artist1");
        published.DurationMs.Should().Be(200_000);
        published.ProgressMs.Should().Be(12_345);
        published.ShuffleEnabled.Should().BeTrue();
        published.RepeatMode.Should().Be(MusicRepeatMode.Track);
        published.VolumePercent.Should().Be(55);
        published.ObservedAt.Should().Be(clock.GetUtcNow());

        // The poller must re-baseline from the documented API on its next pass, so a later natural tick never
        // re-publishes a stale "change" against a baseline this frame already moved past.
        realtime.DrainNudged().Should().Contain(ChannelId);
    }

    /// <summary>
    /// S-MUSIC-5c: the dealer push path is now the PRIMARY transport for playback state (it beats the poller
    /// on every tick), so it must resolve <c>RequestedBy</c> itself rather than leaving the overlay's fast path
    /// silently naming nobody. Uses the exact same resolution <c>MusicService</c>'s poll/mutation-publish legs
    /// use (<see cref="MusicService.RequesterOfPlayingTrack"/>) against the SAME <see cref="ISongRequestQueueStore"/>
    /// singleton — matching the in-flight fair-queue entry's track uri against what the frame reports playing.
    /// </summary>
    [Fact]
    public async Task PlayerStateChangedFrame_PublishesTheRequester_WhenThePlayingTrackWasRequested()
    {
        RecordingEventBus bus = new();
        MusicRealtimeSignal realtime = new();
        FakeTimeProvider clock = new(new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));
        SongRequestQueueStore queueStore = new();
        queueStore.SetInFlight(
            ChannelId.ToString(),
            new(
                "spotify:track:track123",
                "Test Song",
                "Test Artist",
                null,
                200000,
                "viewer1",
                0,
                null,
                ""
            )
        );
        SpotifyDealerConnection connection = NewConnection(bus, realtime, clock, queueStore);

        bool handled = await connection.HandleFrameAsync(
            PlayerStateChangedFrame,
            CancellationToken.None
        );

        handled.Should().BeTrue();
        bus.Published.OfType<PlaybackStateChangedEvent>()
            .Single()
            .RequestedBy.Should()
            .Be("viewer1");
    }

    /// <summary>The negative half: nobody's fair-queue entry matches the track the frame reports playing (the
    /// streamer started it themselves, or the in-flight entry has already moved on) — the overlay must get no
    /// requester, never an empty string.</summary>
    [Fact]
    public async Task PlayerStateChangedFrame_PublishesNoRequester_WhenNobodyRequestedThePlayingTrack()
    {
        RecordingEventBus bus = new();
        MusicRealtimeSignal realtime = new();
        FakeTimeProvider clock = new(new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));
        SpotifyDealerConnection connection = NewConnection(
            bus,
            realtime,
            clock,
            new SongRequestQueueStore() // nothing in flight for this channel
        );

        bool handled = await connection.HandleFrameAsync(
            PlayerStateChangedFrame,
            CancellationToken.None
        );

        handled.Should().BeTrue();
        bus.Published.OfType<PlaybackStateChangedEvent>().Single().RequestedBy.Should().BeNull();
    }

    [Fact]
    public async Task UnrecognisedFrame_IsIgnored_AndPublishesNothing()
    {
        RecordingEventBus bus = new();
        SpotifyDealerConnection connection = NewConnection(
            bus,
            new MusicRealtimeSignal(),
            new(DateTimeOffset.UtcNow)
        );

        bool handled = await connection.HandleFrameAsync(
            """{"type":"pong"}""",
            CancellationToken.None
        );

        handled
            .Should()
            .BeFalse(
                "an undocumented wire format means anything unrecognised is a no-op, not an error"
            );
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Reconnect_UsesDoublingBackoff_AndResubscribesOnEveryFreshConnection()
    {
        // First channel: connection-id handshake, then closes (simulating a drop). Second channel: a fresh
        // connection-id handshake followed by a state-changed frame, then idles.
        ScriptedChannel first = new([ConnectionIdFrame("conn-1")]);
        ScriptedChannel second = new(
            [ConnectionIdFrame("conn-2"), PlayerStateChangedFrame],
            idleAfterScript: true
        );
        ScriptedChannelFactory factory = new(first, second);

        RecordingEventBus bus = new();
        MusicRealtimeSignal realtime = new();
        FakeTimeProvider clock = new(new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));
        RecordingSubscribeHandler subscribeHandler = new();
        HttpClient http = new(subscribeHandler) { BaseAddress = new("https://api.spotify.com") };

        SpotifyDealerConnection connection = new(
            ChannelId,
            factory,
            http,
            _ => Task.FromResult<string?>("test-access-token"),
            bus,
            realtime,
            new SongRequestQueueStore(),
            clock,
            NullLogger.Instance
        );

        using CancellationTokenSource cts = new();
        Task run = connection.RunAsync(cts.Token);

        // Drive virtual time forward until the reconnect (1s base backoff, full-jitter <= 1s) has happened and
        // the second channel's frames have been processed.
        for (
            int i = 0;
            i < 60 && bus.Published.OfType<PlaybackStateChangedEvent>().Count() < 1;
            i++
        )
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(15);
        }

        bus.Published.OfType<PlaybackStateChangedEvent>()
            .Should()
            .ContainSingle(
                "the second channel's state-changed frame must land after the reconnect"
            );

        connection
            .BackoffSchedule.Should()
            .Equal([TimeSpan.FromSeconds(1)], "one drop schedules exactly one base backoff delay");

        subscribeHandler
            .SubscribedConnectionIds.Should()
            .Equal(
                ["conn-1", "conn-2"],
                "the player-notification subscription is re-PUT on every fresh connection, not just the first"
            );

        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException) { }
    }

    private static SpotifyDealerConnection NewConnection(
        RecordingEventBus bus,
        MusicRealtimeSignal realtime,
        FakeTimeProvider clock,
        ISongRequestQueueStore? queueStore = null
    ) =>
        new(
            ChannelId,
            NeverConnectsFactory.Instance,
            new HttpClient(new RecordingSubscribeHandler()),
            _ => Task.FromResult<string?>("test-access-token"),
            bus,
            realtime,
            queueStore ?? new SongRequestQueueStore(),
            clock,
            NullLogger.Instance
        );

    // ── Fakes ────────────────────────────────────────────────────────────────

    /// <summary>Never actually used by <c>HandleFrameAsync</c>-driven tests (they never call <c>RunAsync</c>),
    /// but the constructor requires an <see cref="IWebSocketChannelFactory"/>.</summary>
    private sealed class NeverConnectsFactory : IWebSocketChannelFactory
    {
        public static readonly NeverConnectsFactory Instance = new();

        public Task<IWebSocketChannel> ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not expected to connect in this test.");
    }

    private sealed class RecordingSubscribeHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _subscribed = new();

        public IReadOnlyList<string> SubscribedConnectionIds => [.. _subscribed];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string query = request.RequestUri!.Query.TrimStart('?');
            string? connectionId = query
                .Split('&')
                .Select(pair => pair.Split('=', 2))
                .Where(kv => kv[0] == "connection_id" && kv.Length == 2)
                .Select(kv => Uri.UnescapeDataString(kv[1]))
                .FirstOrDefault();
            if (connectionId is not null)
                _subscribed.Enqueue(connectionId);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ScriptedChannelFactory(params ScriptedChannel[] channels)
        : IWebSocketChannelFactory
    {
        private readonly Queue<ScriptedChannel> _channels = new(channels);

        public Task<IWebSocketChannel> ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            ScriptedChannel channel =
                _channels.Count > 0 ? _channels.Dequeue() : new([], idleAfterScript: true);
            return Task.FromResult<IWebSocketChannel>(channel);
        }
    }

    /// <summary>An in-memory <see cref="IWebSocketChannel"/> yielding a fixed script of frames, then either
    /// closing (default — drives a reconnect) or idling forever (blocks until cancelled).</summary>
    private sealed class ScriptedChannel(IReadOnlyList<string> frames, bool idleAfterScript = false)
        : IWebSocketChannel
    {
        private readonly Queue<string> _frames = new(frames);
        private readonly TaskCompletionSource _idle = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken
        )
        {
            if (_frames.Count > 0)
            {
                byte[] payload = Encoding.UTF8.GetBytes(_frames.Dequeue());
                payload.CopyTo(buffer.Array!, buffer.Offset);
                return Task.FromResult(
                    new WebSocketReceiveResult(payload.Length, WebSocketMessageType.Text, true)
                );
            }

            if (!idleAfterScript)
                return Task.FromResult(
                    new WebSocketReceiveResult(0, WebSocketMessageType.Close, true)
                );

            return WaitIdleAsync(cancellationToken);
        }

        private async Task<WebSocketReceiveResult> WaitIdleAsync(
            CancellationToken cancellationToken
        )
        {
            await using (cancellationToken.Register(() => _idle.TrySetResult()))
                await _idle.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return new(0, WebSocketMessageType.Close, true);
        }

        public ValueTask DisposeAsync()
        {
            _idle.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
