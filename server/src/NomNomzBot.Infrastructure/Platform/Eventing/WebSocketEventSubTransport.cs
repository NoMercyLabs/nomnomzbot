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
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform.Enums;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The WebSocket EventSub transport (lite / self-host, twitch-eventsub §3.3). Owns the wire: a single
/// <see cref="ClientWebSocket"/> to <c>wss://eventsub.wss.twitch.tv/ws</c>, the receive loop, keepalive-timeout
/// detection, and reconnect with exponential backoff + jitter. Frame parsing is System.Text.Json on the
/// untrusted hot path. Subscription create/delete/list ride the shared <see cref="ITwitchHelixTransport"/>
/// (auth header, resilience, rate limiting layered there). Lifecycle decisions (when to (re)register, the
/// post-welcome reconcile) belong to the hosted service via <see cref="IEventSubNotificationSink"/>.
/// <para>
/// The socket is created through a <see cref="IWebSocketChannelFactory"/> seam so tests drive the lifecycle
/// over an in-memory channel without touching Twitch.
/// </para>
/// </summary>
public sealed class WebSocketEventSubTransport : IEventSubTransport
{
    private const string DefaultWsUrl =
        "wss://eventsub.wss.twitch.tv/ws?keepalive_timeout_seconds=30";

    // Twitch's EventSub frames are snake_case (message_type, reconnect_url, keepalive_timeout_seconds, …);
    // the wire DTOs are PascalCase, so the snake_case naming policy + case-insensitivity binds them.
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IWebSocketChannelFactory _channelFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventSubConditionBuilder _conditionBuilder;
    private readonly TimeProvider _clock;
    private readonly ILogger<WebSocketEventSubTransport> _logger;

    private readonly Lock _stateLock = new();
    private IEventSubNotificationSink? _sink;
    private IWebSocketChannel? _channel;
    private CancellationTokenSource? _runCts;
    private Task? _receiveLoop;

    private volatile string? _sessionId;
    private TimeSpan _keepaliveTimeout = TimeSpan.FromSeconds(40);
    private DateTimeOffset _lastFrameAt;
    private DateTimeOffset? _lastReconnectAt;
    private TimeSpan _currentBackoff = TimeSpan.Zero;

    public WebSocketEventSubTransport(
        IWebSocketChannelFactory channelFactory,
        IServiceScopeFactory scopeFactory,
        IEventSubConditionBuilder conditionBuilder,
        TimeProvider clock,
        ILogger<WebSocketEventSubTransport> logger
    )
    {
        _channelFactory = channelFactory;
        _scopeFactory = scopeFactory;
        _conditionBuilder = conditionBuilder;
        _clock = clock;
        _logger = logger;
    }

    // The Helix transport is SCOPED (it reads the per-request DbContext for token resolution); this transport
    // is a SINGLETON, so it resolves a fresh Helix client inside a short-lived scope per subscription call
    // rather than capturing one (which ValidateScopes rightly rejects).
    private async Task<TResult> WithHelixAsync<TResult>(
        Func<ITwitchHelixTransport, Task<TResult>> call
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ITwitchHelixTransport helix =
            scope.ServiceProvider.GetRequiredService<ITwitchHelixTransport>();
        return await call(helix);
    }

    public EventSubTransportKind Kind => EventSubTransportKind.WebSocket;

    /// <summary>The current WebSocket session id (null until welcome).</summary>
    public string? SessionId => _sessionId;

    /// <summary>When the transport last reconnected (null if never).</summary>
    public DateTimeOffset? LastReconnectAt => _lastReconnectAt;

    /// <summary>The current backoff delay scheduled for the next reconnect (zero while connected).</summary>
    public TimeSpan CurrentBackoff => _currentBackoff;

    /// <summary>
    /// Binds the lifecycle sink (the hosted service). Set once before <see cref="StartAsync"/>; the receive
    /// loop forwards welcome / notification / revocation frames to it.
    /// </summary>
    public void BindSink(IEventSubNotificationSink sink) => _sink = sink;

    public async Task<Result<EventSubTransportHandle>> StartAsync(CancellationToken ct = default)
    {
        // Idempotent: if a session is already live, return its handle.
        if (_sessionId is { } live)
            return Result.Success(new EventSubTransportHandle { Kind = Kind, SessionId = live });

        TaskCompletionSource<string> welcome = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        lock (_stateLock)
        {
            _runCts?.Cancel();
            _runCts = new CancellationTokenSource();
        }

        CancellationToken runToken = _runCts.Token;
        _receiveLoop = Task.Run(() => RunWithReconnectAsync(welcome, runToken), runToken);

        // Wait for the first welcome (or cancellation) so the caller gets a usable handle.
        Task completed = await Task.WhenAny(welcome.Task, Task.Delay(Timeout.Infinite, ct));
        if (completed != welcome.Task)
            return Result.Failure<EventSubTransportHandle>(
                "EventSub WebSocket start cancelled before welcome.",
                "SERVICE_UNAVAILABLE"
            );

        string sessionId = await welcome.Task;
        return Result.Success(new EventSubTransportHandle { Kind = Kind, SessionId = sessionId });
    }

    private async Task RunWithReconnectAsync(
        TaskCompletionSource<string> firstWelcome,
        CancellationToken ct
    )
    {
        string connectUrl = DefaultWsUrl;
        TimeSpan backoff = TimeSpan.FromSeconds(1);

        while (!ct.IsCancellationRequested)
        {
            string disconnectReason = "unknown";
            try
            {
                connectUrl = await ConnectAndReceiveAsync(connectUrl, firstWelcome, ct);
                backoff = TimeSpan.FromSeconds(1); // a clean reconnect-url swap resets backoff
                _currentBackoff = TimeSpan.Zero;
                continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (KeepaliveTimeoutException)
            {
                disconnectReason = "keepalive_timeout";
            }
            catch (Exception ex)
            {
                disconnectReason = Scrub(ex.GetType().Name);
                _logger.LogWarning(
                    "EventSub WS dropped ({Reason}); reconnecting in {Backoff:g}",
                    disconnectReason,
                    backoff
                );
            }

            _sessionId = null;
            if (ct.IsCancellationRequested)
                break;

            // Exponential backoff capped at 64 s, plus full jitter, so a fleet does not thunder Twitch.
            TimeSpan jittered = WithJitter(backoff);
            _currentBackoff = jittered;
            _ = disconnectReason; // surfaced via the hosted service's disconnected event in a later step
            try
            {
                await Task.Delay(jittered, _clock, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 64));
            connectUrl = DefaultWsUrl;
        }
    }

    /// <summary>Connects, drives the receive loop, and returns the next URL (reconnect-url or default).</summary>
    private async Task<string> ConnectAndReceiveAsync(
        string url,
        TaskCompletionSource<string> firstWelcome,
        CancellationToken ct
    )
    {
        IWebSocketChannel channel = await _channelFactory.ConnectAsync(new Uri(url), ct);

        // Swap atomically and tear down the superseded channel (server-requested reconnect keeps the old
        // socket only until the new one is connected — then it is closed, no leak, no duplicate stream).
        IWebSocketChannel? previous;
        lock (_stateLock)
        {
            previous = _channel;
            _channel = channel;
        }
        if (previous is not null)
            await previous.DisposeAsync();

        if (!url.Equals(DefaultWsUrl, StringComparison.Ordinal))
            _lastReconnectAt = _clock.GetUtcNow();

        _lastFrameAt = _clock.GetUtcNow();
        byte[] buffer = new byte[64 * 1024];
        StringBuilder frame = new();

        while (!ct.IsCancellationRequested)
        {
            frame.Clear();
            WebSocketReceiveResult result;
            do
            {
                using CancellationTokenSource keepaliveCts =
                    CancellationTokenSource.CreateLinkedTokenSource(ct);
                using CancellationTokenSource keepaliveTimer = new(_keepaliveTimeout, _clock);
                using CancellationTokenRegistration link = keepaliveTimer.Token.Register(
                    static state => ((CancellationTokenSource)state!).Cancel(),
                    keepaliveCts
                );

                try
                {
                    result = await channel.ReceiveAsync(buffer, keepaliveCts.Token);
                }
                catch (OperationCanceledException)
                    when (!ct.IsCancellationRequested && keepaliveCts.IsCancellationRequested)
                {
                    throw new KeepaliveTimeoutException();
                }

                if (result.MessageType == WebSocketMessageType.Close)
                    return DefaultWsUrl;

                frame.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            _lastFrameAt = _clock.GetUtcNow();
            string? reconnectUrl = await HandleFrameAsync(frame.ToString(), firstWelcome, ct);
            if (reconnectUrl is not null)
                return reconnectUrl;
        }

        return DefaultWsUrl;
    }

    /// <summary>Routes one parsed wire frame by its <c>message_type</c>. Returns a reconnect URL when told to swap.</summary>
    private async Task<string?> HandleFrameAsync(
        string json,
        TaskCompletionSource<string> firstWelcome,
        CancellationToken ct
    )
    {
        WireEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<WireEnvelope>(json, WireJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "EventSub WS: malformed frame");
            return null;
        }

        if (envelope?.Metadata is null)
            return null;

        switch (envelope.Metadata.MessageType)
        {
            case "session_welcome":
            {
                string? sessionId = envelope.Payload?.Session?.Id;
                if (sessionId is null)
                    break;

                if (envelope.Payload?.Session?.KeepaliveTimeoutSeconds is { } keepalive and > 0)
                    _keepaliveTimeout = TimeSpan.FromSeconds(keepalive + 5);

                _sessionId = sessionId;
                firstWelcome.TrySetResult(sessionId);
                if (_sink is not null)
                    await _sink.OnSessionWelcomeAsync(sessionId, ct);
                break;
            }

            case "session_keepalive":
                // Liveness only; _lastFrameAt already advanced by the caller.
                break;

            case "session_reconnect":
            {
                string? reconnectUrl = envelope.Payload?.Session?.ReconnectUrl;
                _logger.LogInformation("EventSub WS: server requested reconnect");
                return reconnectUrl ?? DefaultWsUrl;
            }

            case "revocation":
            {
                Wire.Subscription? sub = envelope.Payload?.Subscription;
                if (sub is not null && _sink is not null)
                    await _sink.OnRevocationAsync(
                        sub.Id ?? string.Empty,
                        sub.Type ?? string.Empty,
                        sub.Status ?? "authorization_revoked",
                        ExtractBroadcasterId(envelope.Payload?.Event),
                        ct
                    );
                break;
            }

            case "notification":
            {
                if (_sink is null)
                    break;

                Wire.Subscription? sub = envelope.Payload?.Subscription;
                JsonElement? @event = envelope.Payload?.Event;
                if (sub?.Type is null || @event is null)
                    break;

                await _sink.OnNotificationAsync(
                    envelope.Metadata.MessageId ?? Guid.NewGuid().ToString(),
                    ParseTimestamp(envelope.Metadata.MessageTimestamp),
                    sub.Type,
                    sub.Version ?? "1",
                    ExtractBroadcasterId(@event),
                    @event.Value,
                    ct
                );
                break;
            }
        }

        return null;
    }

    public async Task<Result<TwitchSubscriptionResult>> CreateSubscriptionAsync(
        EventSubSubscriptionRequest request,
        EventSubTransportHandle handle,
        CancellationToken ct = default
    )
    {
        if (handle.SessionId is null)
            return Result.Failure<TwitchSubscriptionResult>(
                "Cannot create a WebSocket subscription without a session id.",
                "SERVICE_UNAVAILABLE"
            );

        var body = new
        {
            type = request.EventType,
            version = request.Version,
            condition = request.Condition,
            transport = new { method = "websocket", session_id = handle.SessionId },
        };

        // Broadcaster-scoped topics ride the broadcaster's user token; app-scoped topics ride the bot/app token.
        bool needsUserToken = _conditionBuilder.RequiresBroadcasterToken(request.EventType);
        TwitchHelixRequest helixRequest = new(
            HttpMethod.Post,
            "eventsub/subscriptions",
            needsUserToken ? TwitchHelixAuth.User : TwitchHelixAuth.App,
            needsUserToken ? request.BroadcasterId : null,
            Body: body
        );

        Result<TwitchEventSubWireSubscription> sent = await WithHelixAsync(helix =>
            helix.SendWithResultAsync<TwitchEventSubWireSubscription>(helixRequest, ct)
        );
        if (sent.IsFailure)
            return Result.Failure<TwitchSubscriptionResult>(
                sent.ErrorMessage!,
                sent.ErrorCode,
                sent.ErrorDetail
            );

        TwitchEventSubWireSubscription wire = sent.Value;
        return Result.Success(
            new TwitchSubscriptionResult
            {
                TwitchSubscriptionId = wire.Id ?? string.Empty,
                Type = wire.Type ?? request.EventType,
                Version = wire.Version ?? request.Version,
                Status = wire.Status ?? "enabled",
                Cost = wire.Cost ?? 0,
                SessionId = handle.SessionId,
            }
        );
    }

    public async Task<Result> DeleteSubscriptionAsync(
        string twitchSubscriptionId,
        CancellationToken ct = default
    )
    {
        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "eventsub/subscriptions",
            TwitchHelixAuth.App,
            Query: [new KeyValuePair<string, string>("id", twitchSubscriptionId)]
        );

        Result deleted = await WithHelixAsync(helix => helix.SendAsync(request, ct));
        // A 404 means it is already gone — idempotent success.
        if (
            deleted.IsFailure
            && deleted.ErrorCode
                == NomNomzBot.Application.Contracts.Twitch.TwitchErrorCodes.NotFound
        )
            return Result.Success();
        return deleted;
    }

    public async Task<Result<IReadOnlyList<TwitchSubscriptionResult>>> ListSubscriptionsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<TwitchSubscriptionResult> all = [];
        string? cursor = null;

        do
        {
            List<KeyValuePair<string, string>> query = [];
            if (cursor is not null)
                query.Add(new KeyValuePair<string, string>("after", cursor));

            TwitchHelixRequest request = new(
                HttpMethod.Get,
                "eventsub/subscriptions",
                TwitchHelixAuth.App,
                Query: query.Count > 0 ? query : null
            );

            Result<TwitchPage<TwitchEventSubWireSubscription>> page = await WithHelixAsync(helix =>
                helix.GetPageAsync<TwitchEventSubWireSubscription>(request, ct)
            );
            if (page.IsFailure)
                return Result.Failure<IReadOnlyList<TwitchSubscriptionResult>>(
                    page.ErrorMessage!,
                    page.ErrorCode,
                    page.ErrorDetail
                );

            foreach (TwitchEventSubWireSubscription wire in page.Value.Items)
                all.Add(
                    new TwitchSubscriptionResult
                    {
                        TwitchSubscriptionId = wire.Id ?? string.Empty,
                        Type = wire.Type ?? string.Empty,
                        Version = wire.Version ?? "1",
                        Status = wire.Status ?? "enabled",
                        Cost = wire.Cost ?? 0,
                        SessionId = wire.Transport?.SessionId,
                    }
                );

            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));

        return Result.Success<IReadOnlyList<TwitchSubscriptionResult>>(all);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        CancellationTokenSource? cts;
        IWebSocketChannel? channel;
        lock (_stateLock)
        {
            cts = _runCts;
            channel = _channel;
            _runCts = null;
            _channel = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop;
            }
            catch (OperationCanceledException) { }
        }

        if (channel is not null)
            await channel.DisposeAsync();

        _sessionId = null;
    }

    private TimeSpan WithJitter(TimeSpan baseDelay)
    {
        // Full jitter (AWS recipe): uniform in [0, baseDelay]. Avoids synchronized fleet reconnects.
        double seconds = baseDelay.TotalSeconds * Random.Shared.NextDouble();
        return TimeSpan.FromSeconds(seconds);
    }

    private static string ExtractBroadcasterId(JsonElement? @event)
    {
        if (@event is not { } element || element.ValueKind != JsonValueKind.Object)
            return string.Empty;

        foreach (string key in BroadcasterIdKeys)
            if (
                element.TryGetProperty(key, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
            )
                return value.GetString() ?? string.Empty;

        return string.Empty;
    }

    // Every channel.*-prefixed topic's event body carries broadcaster_user_id (or, for raids, only
    // to_broadcaster_user_id) — those two keys resolve the tenant for the whole channel-plane surface. The two
    // user-plane topics (user.update, user.whisper.message) carry no broadcaster id at all: user.update's event
    // names the subscribed user as user_id, and user.whisper.message names the whisper's recipient as
    // to_user_id. Both resolve correctly whenever the subscribing identity IS the channel (self-host, or the
    // fallback-to-broadcaster case in EventSubConditionBuilder) — a dedicated bot account shared across
    // channels cannot be disambiguated this way, since the condition (and so the id on the wire) is identical
    // for every channel that bot serves.
    private static readonly string[] BroadcasterIdKeys =
    [
        "broadcaster_user_id",
        "to_broadcaster_user_id",
        "user_id",
        "to_user_id",
    ];

    private static DateTimeOffset ParseTimestamp(string? raw) =>
        DateTimeOffset.TryParse(raw, out DateTimeOffset parsed) ? parsed : DateTimeOffset.UtcNow;

    private static string Scrub(string reason) => reason;

    private sealed class KeepaliveTimeoutException : Exception;

    // ── Wire frame shapes (System.Text.Json, snake_case Web defaults) ──
    private sealed class WireEnvelope
    {
        public Wire.Metadata? Metadata { get; init; }
        public Wire.Payload? Payload { get; init; }
    }

    private static class Wire
    {
        public sealed class Metadata
        {
            public string? MessageId { get; init; }
            public string? MessageType { get; init; }
            public string? MessageTimestamp { get; init; }
        }

        public sealed class Payload
        {
            public Session? Session { get; init; }
            public Subscription? Subscription { get; init; }
            public JsonElement? Event { get; init; }
        }

        public sealed class Session
        {
            public string? Id { get; init; }
            public string? ReconnectUrl { get; init; }
            public int? KeepaliveTimeoutSeconds { get; init; }
        }

        public sealed class Subscription
        {
            public string? Id { get; init; }
            public string? Type { get; init; }
            public string? Version { get; init; }
            public string? Status { get; init; }
        }
    }
}
