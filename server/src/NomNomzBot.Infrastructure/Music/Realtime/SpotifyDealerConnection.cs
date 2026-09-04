// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Music.Realtime;

/// <summary>
/// One channel's live connection to Spotify's realtime dealer socket (<c>wss://dealer.spotify.com/</c>) — the
/// same undocumented endpoint the Spotify web player itself uses, reverse-engineered from the legacy reference
/// (<c>nomercy-bot</c>'s <c>SpotifyWebsocketService</c>). Publishes <see cref="PlaybackStateChangedEvent"/> the
/// instant a <c>PLAYER_STATE_CHANGED</c> frame lands, closing the poll-cadence-plus-widget-interpolation gap
/// the owner sees on the overlay. Ship-ToS-gray on incumbent precedent (project standing rule) — the wire
/// format can change under us at any time, so <see cref="SpotifyDealerFrameParser"/> silently ignores anything
/// it does not recognise instead of throwing, and <c>MusicStatePollingService</c> (the documented-API poller)
/// remains the fallback of record: <see cref="SpotifyDealerHostedService"/> never blocks it, and a dealer
/// publish also nudges it (<see cref="IMusicRealtimeSignal"/>) so its own dedupe snapshot re-baselines from the
/// documented API on the very next pass rather than trusting the undocumented frame as the source of truth.
/// <para>
/// Handshake: connect → the socket immediately sends a frame carrying a <c>Spotify-Connection-Id</c> header →
/// PUT <c>/v1/me/notifications/player?connection_id=</c> subscribes this connection to state-change pushes →
/// <c>PLAYER_STATE_CHANGED</c> cluster frames follow. Reconnects with exponential backoff + jitter on any drop,
/// mirroring <c>WebSocketEventSubTransport</c>'s <c>WsSession</c> — the in-repo pattern for a reconnecting
/// hosted WebSocket — rather than inventing a second style.
/// </para>
/// </summary>
internal sealed class SpotifyDealerConnection
{
    private const string DealerBaseUrl = "wss://dealer.spotify.com/";
    private const string SubscribeUrl = "https://api.spotify.com/v1/me/notifications/player";

    private readonly Guid _broadcasterId;
    private readonly IWebSocketChannelFactory _channelFactory;
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string?>> _resolveAccessTokenAsync;
    private readonly IEventBus _eventBus;
    private readonly IMusicRealtimeSignal _realtime;
    private readonly ISongRequestQueueStore _queueStore;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    // Every base (pre-jitter) backoff delay scheduled so far, in issue order — a test/diagnostics seam for
    // the doubling schedule, same shape as WebSocketEventSubTransport.GetBackoffScheduleForOwner.
    private readonly List<TimeSpan> _backoffSchedule = [];
    private readonly Lock _backoffLock = new();

    // The access token used for the CURRENT socket, so a frame handled off the receive loop (production) or
    // fed directly by a test (HandleFrameAsync) can PUT the subscribe call without re-resolving it.
    private string? _currentAccessToken;

    internal SpotifyDealerConnection(
        Guid broadcasterId,
        IWebSocketChannelFactory channelFactory,
        HttpClient http,
        Func<CancellationToken, Task<string?>> resolveAccessTokenAsync,
        IEventBus eventBus,
        IMusicRealtimeSignal realtime,
        ISongRequestQueueStore queueStore,
        TimeProvider clock,
        ILogger logger
    )
    {
        _broadcasterId = broadcasterId;
        _channelFactory = channelFactory;
        _http = http;
        _resolveAccessTokenAsync = resolveAccessTokenAsync;
        _eventBus = eventBus;
        _realtime = realtime;
        _queueStore = queueStore;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Diagnostic + test seam: every base backoff delay scheduled so far, in issue order.</summary>
    internal IReadOnlyList<TimeSpan> BackoffSchedule
    {
        get
        {
            lock (_backoffLock)
                return [.. _backoffSchedule];
        }
    }

    /// <summary>Runs the connect → receive → reconnect-with-backoff loop until <paramref name="ct"/> fires.</summary>
    internal async Task RunAsync(CancellationToken ct)
    {
        TimeSpan backoff = TimeSpan.FromSeconds(1);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceiveAsync(ct);
                backoff = TimeSpan.FromSeconds(1); // a clean pass (loop exited via cancellation) resets backoff
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Spotify dealer socket (channel {ChannelId}) dropped; reconnecting in {Backoff:g}",
                    _broadcasterId,
                    backoff
                );
            }

            if (ct.IsCancellationRequested)
                return;

            lock (_backoffLock)
                _backoffSchedule.Add(backoff);

            try
            {
                await Task.Delay(WithJitter(backoff), _clock, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 64));
        }
    }

    private async Task ConnectAndReceiveAsync(CancellationToken ct)
    {
        string? accessToken = await _resolveAccessTokenAsync(ct);
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException(
                $"No usable Spotify access token for channel {_broadcasterId} — dealer socket unavailable."
            );

        _currentAccessToken = accessToken;

        Uri uri = new($"{DealerBaseUrl}?access_token={Uri.EscapeDataString(accessToken)}");
        await using IWebSocketChannel channel = await _channelFactory.ConnectAsync(uri, ct);

        byte[] buffer = new byte[64 * 1024];
        StringBuilder frame = new();

        while (!ct.IsCancellationRequested)
        {
            frame.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await channel.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException(
                        $"Spotify dealer socket (channel {_broadcasterId}) closed by server."
                    );

                frame.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            await HandleFrameAsync(frame.ToString(), ct);
        }
    }

    /// <summary>
    /// Routes one raw dealer frame: the connection-id handshake triggers the subscribe PUT, a
    /// <c>PLAYER_STATE_CHANGED</c> frame publishes. Production and test seam alike — the receive loop and a
    /// unit test both drive frames through this same path, so what a test proves is exactly what production
    /// runs. Returns true when the frame was recognised, false when it was silently ignored.
    /// </summary>
    internal async Task<bool> HandleFrameAsync(string rawFrame, CancellationToken ct)
    {
        if (SpotifyDealerFrameParser.TryGetConnectionId(rawFrame, out string? connectionId))
        {
            await SubscribeAsync(connectionId!, ct);
            return true;
        }

        if (
            SpotifyDealerFrameParser.TryParsePlayerStateChanged(
                rawFrame,
                out SpotifyPlayerStateChangedFrame? state
            )
        )
        {
            await PublishAsync(state!, ct);
            return true;
        }

        return false;
    }

    private async Task PublishAsync(SpotifyPlayerStateChangedFrame state, CancellationToken ct)
    {
        await _eventBus.PublishAsync(
            new PlaybackStateChangedEvent
            {
                BroadcasterId = _broadcasterId,
                IsPlaying = state.IsPlaying,
                TrackName = state.TrackName,
                Artist = state.Artist,
                Album = state.Album,
                AlbumArtUrl = state.AlbumArtUrl,
                DurationMs = state.DurationMs,
                ProgressMs = state.ProgressMs,
                Provider = "spotify",
                TrackUri = state.TrackUri,
                ArtistId = state.ArtistId,
                // Same resolution MusicService's poll/mutation-publish legs use (RequesterOfPlayingTrack) —
                // matches the in-flight fair-queue entry's track uri against what the dealer just reported
                // playing, so the overlay never attributes a track to whoever last requested SOMETHING.
                RequestedBy = MusicService.RequesterOfPlayingTrack(
                    _queueStore,
                    _broadcasterId.ToString(),
                    state.TrackUri
                ),
                ShuffleEnabled = state.ShuffleEnabled,
                RepeatMode = state.RepeatMode,
                VolumePercent = state.VolumePercent,
                ObservedAt = _clock.GetUtcNow(),
            },
            ct
        );

        // The poller re-baselines its own dedupe snapshot from the documented Web API on its very next pass,
        // so a later natural poll tick never re-publishes a stale-looking "change" against a baseline this
        // undocumented frame already moved past.
        _realtime.Nudge(_broadcasterId);
    }

    private async Task SubscribeAsync(string connectionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_currentAccessToken))
            return;

        try
        {
            using HttpRequestMessage request = new(
                HttpMethod.Put,
                $"{SubscribeUrl}?connection_id={Uri.EscapeDataString(connectionId)}"
            );
            request.Headers.Authorization = new("Bearer", _currentAccessToken);

            using HttpResponseMessage response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning(
                    "Spotify dealer subscribe failed for channel {ChannelId}: {Status}",
                    _broadcasterId,
                    response.StatusCode
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Spotify dealer subscribe threw for channel {ChannelId}",
                _broadcasterId
            );
        }
    }

    private static TimeSpan WithJitter(TimeSpan baseDelay)
    {
        // Full jitter (AWS recipe): uniform in [0, baseDelay] — same recipe as WebSocketEventSubTransport.
        double seconds = baseDelay.TotalSeconds * Random.Shared.NextDouble();
        return TimeSpan.FromSeconds(seconds);
    }
}
