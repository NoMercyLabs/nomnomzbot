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
/// The WebSocket EventSub transport (lite / self-host, twitch-eventsub §3.3). Owns the wire.
/// <para>
/// Twitch forbids subscriptions created by different users on one WebSocket session
/// ("websocket transport cannot have subscriptions created by different users"), so the transport keeps ONE
/// <see cref="WsSession"/> per token owner (<see cref="EventSubOwnerKeys"/>): the bot's session carries every
/// channel's chat-read topics (bot token), and each broadcaster gets their own session for their authorized
/// topics (that broadcaster's token). Each session owns its own <see cref="ClientWebSocket"/> to
/// <c>wss://eventsub.wss.twitch.tv/ws</c>, receive loop, keepalive-timeout detection, and reconnect with
/// exponential backoff + jitter. Subscription create/delete/list ride the shared
/// <see cref="ITwitchHelixTransport"/> (auth header, resilience, rate limiting layered there).
/// </para>
/// <para>
/// The socket is created through a <see cref="IWebSocketChannelFactory"/> seam so tests drive the lifecycle over
/// an in-memory channel without touching Twitch. Lifecycle decisions (when to (re)register, the post-welcome
/// reconcile) belong to the hosted service via <see cref="IEventSubNotificationSink"/>, which the transport calls
/// with the owner key so the service re-registers only that session's slice.
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

    // One live WebSocket session per token owner (bot + one per broadcaster). Keyed by EventSubOwnerKeys.
    private readonly ConcurrentDictionary<string, WsSession> _sessions = new();
    private IEventSubNotificationSink? _sink;

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

    /// <summary>The bot session's current id (null until its welcome). Back-compat for health + tests.</summary>
    public string? SessionId =>
        _sessions.TryGetValue(EventSubOwnerKeys.Bot, out WsSession? bot)
            ? bot.SessionId
            : _sessions.Values.Select(s => s.SessionId).FirstOrDefault(id => id is not null);

    /// <summary>The live session id for a token owner, or null when no session is currently open for it.</summary>
    public string? CurrentSessionId(string ownerKey) =>
        _sessions.TryGetValue(ownerKey, out WsSession? session) ? session.SessionId : null;

    /// <summary>When any session last reconnected (null if never).</summary>
    public DateTimeOffset? LastReconnectAt =>
        _sessions
            .Values.Select(s => s.LastReconnectAt)
            .Where(t => t is not null)
            .DefaultIfEmpty(null)
            .Max();

    /// <summary>
    /// Every token-owner key with a session ever opened this process (bot + one per broadcaster),
    /// snapshotted at call time. Used by <see cref="ReconnectAsync"/> callers (twitch-eventsub §7 hardening) to
    /// re-open EVERY owner's session after a full transport restart — not just the bot's default session.
    /// </summary>
    public IReadOnlyCollection<string> KnownOwnerKeys => [.. _sessions.Keys];

    /// <summary>
    /// The base (pre-jitter) backoff delay scheduled before each reconnect attempt for the given owner's
    /// session, in issue order (e.g. 1s, 2s, 4s, 8s, … capped at 64s). Diagnostic + test seam: full jitter makes
    /// the ACTUAL delay random, but the doubling schedule itself is deterministic and worth asserting on.
    /// </summary>
    public IReadOnlyList<TimeSpan> GetBackoffScheduleForOwner(string ownerKey) =>
        _sessions.TryGetValue(ownerKey, out WsSession? session) ? session.BackoffSchedule : [];

    /// <summary>
    /// Binds the lifecycle sink (the hosted service). Set once before <see cref="StartAsync"/>; each session's
    /// receive loop forwards welcome / notification / revocation frames to it (welcome carries the owner key).
    /// </summary>
    public void BindSink(IEventSubNotificationSink sink) => _sink = sink;

    public Task<Result<EventSubTransportHandle>> StartAsync(CancellationToken ct = default) =>
        EnsureSessionAsync(EventSubOwnerKeys.Bot, ct);

    public async Task<Result<EventSubTransportHandle>> EnsureSessionAsync(
        string ownerKey,
        CancellationToken ct = default
    )
    {
        WsSession session = _sessions.GetOrAdd(
            ownerKey,
            key => new(key, _channelFactory, _clock, _logger, this)
        );

        Result<string> sessionId = await session.EnsureStartedAsync(ct);
        return sessionId.IsFailure
            ? Result.Failure<EventSubTransportHandle>(sessionId.ErrorMessage!, sessionId.ErrorCode)
            : Result.Success(
                new EventSubTransportHandle { Kind = Kind, SessionId = sessionId.Value }
            );
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
            Query: [new("id", twitchSubscriptionId)]
        );

        Result deleted = await WithHelixAsync(helix => helix.SendAsync(request, ct));
        // A 404 means it is already gone — idempotent success.
        if (deleted is { IsFailure: true, ErrorCode: TwitchErrorCodes.NotFound })
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
                query.Add(new("after", cursor));

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
                    new()
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
        List<WsSession> sessions = [.. _sessions.Values];
        _sessions.Clear();
        foreach (WsSession session in sessions)
            await session.StopAsync();
    }

    // ── Sink forwarding (called by every WsSession receive loop) ────────────────

    private Task ForwardWelcomeAsync(string ownerKey, string sessionId, CancellationToken ct) =>
        _sink?.OnSessionWelcomeAsync(sessionId, ownerKey, ct) ?? Task.CompletedTask;

    private Task ForwardNotificationAsync(
        string messageId,
        DateTimeOffset messageTimestamp,
        string subscriptionType,
        string subscriptionVersion,
        string twitchBroadcasterUserId,
        JsonElement @event,
        CancellationToken ct
    ) =>
        _sink?.OnNotificationAsync(
            messageId,
            messageTimestamp,
            subscriptionType,
            subscriptionVersion,
            twitchBroadcasterUserId,
            @event,
            ct
        ) ?? Task.CompletedTask;

    private Task ForwardDisconnectedAsync(
        string ownerKey,
        string? sessionId,
        string reason,
        TimeSpan nextRetryIn,
        CancellationToken ct
    ) =>
        _sink?.OnSessionDisconnectedAsync(ownerKey, sessionId, reason, nextRetryIn, ct)
        ?? Task.CompletedTask;

    private Task ForwardRevocationAsync(
        string twitchSubscriptionId,
        string subscriptionType,
        string status,
        string twitchBroadcasterUserId,
        CancellationToken ct
    ) =>
        _sink?.OnRevocationAsync(
            twitchSubscriptionId,
            subscriptionType,
            status,
            twitchBroadcasterUserId,
            ct
        ) ?? Task.CompletedTask;

    // ── One WebSocket session (one token owner) ─────────────────────────────────

    /// <summary>
    /// A single WebSocket connection to Twitch EventSub for one token owner: connect, receive loop, keepalive
    /// detection, and reconnect with backoff + jitter. Forwards frames to the owning transport (which fans out to
    /// the sink), tagging the welcome with this session's <see cref="_ownerKey"/> so the hosted service
    /// re-registers only this owner's slice.
    /// </summary>
    private sealed class WsSession
    {
        private readonly string _ownerKey;
        private readonly IWebSocketChannelFactory _channelFactory;
        private readonly TimeProvider _clock;
        private readonly ILogger _logger;
        private readonly WebSocketEventSubTransport _owner;

        private readonly Lock _stateLock = new();
        private IWebSocketChannel? _channel;
        private CancellationTokenSource? _runCts;
        private Task? _receiveLoop;

        private volatile string? _sessionId;
        private TimeSpan _keepaliveTimeout = TimeSpan.FromSeconds(40);
        private DateTimeOffset? _lastReconnectAt;

        // Every base (pre-jitter) backoff delay scheduled so far, in issue order — the doubling schedule is
        // deterministic even though WithJitter randomizes the ACTUAL wait, so this is what a test (or an
        // operator reading diagnostics) can assert against.
        private readonly List<TimeSpan> _backoffSchedule = [];

        // Completed by the receive loop on the FIRST welcome. Shared across concurrent EnsureStartedAsync callers
        // (the hosted-service start and BotLifecycleService both open the bot session on boot) so the second
        // caller awaits the SAME signal instead of a private TCS the loop never completes — which would hang.
        private TaskCompletionSource<string>? _startWelcome;

        public WsSession(
            string ownerKey,
            IWebSocketChannelFactory channelFactory,
            TimeProvider clock,
            ILogger logger,
            WebSocketEventSubTransport owner
        )
        {
            _ownerKey = ownerKey;
            _channelFactory = channelFactory;
            _clock = clock;
            _logger = logger;
            _owner = owner;
        }

        public string? SessionId => _sessionId;
        public DateTimeOffset? LastReconnectAt => _lastReconnectAt;

        /// <summary>Snapshot of every base backoff delay scheduled so far, in issue order.</summary>
        public IReadOnlyList<TimeSpan> BackoffSchedule
        {
            get
            {
                lock (_stateLock)
                    return [.. _backoffSchedule];
            }
        }

        public async Task<Result<string>> EnsureStartedAsync(CancellationToken ct)
        {
            // Idempotent: a live session just returns its id.
            if (_sessionId is { } live)
                return Result.Success(live);

            Task<string> welcomeTask;
            lock (_stateLock)
            {
                // Won the race to a live id between the check above and the lock — reuse it.
                if (_sessionId is { } already)
                    return Result.Success(already);

                // Start the receive loop on first entry; concurrent callers fall through to await the SAME
                // welcome the loop will complete (a private TCS would never be signaled → the caller hangs).
                if (_receiveLoop is null || _startWelcome is null)
                {
                    _startWelcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _runCts?.Cancel();
                    _runCts = new();
                    CancellationToken runToken = _runCts.Token;
                    TaskCompletionSource<string> welcome = _startWelcome;
                    _receiveLoop = Task.Run(
                        () => RunWithReconnectAsync(welcome, runToken),
                        runToken
                    );
                }
                welcomeTask = _startWelcome.Task;
            }

            // Wait for the first welcome (or cancellation) so the caller gets a usable session id. WaitAsync(ct)
            // registers a single lightweight callback on ct and disposes it the moment EITHER side completes —
            // unlike the previous Task.WhenAny(welcomeTask, Task.Delay(Timeout.Infinite, ct)), whose Delay task
            // (and its ct registration) stayed alive, unobserved, for the lifetime of ct whenever welcomeTask won
            // the race first (the common case: BotLifecycleService and the hosted service both start the bot
            // session on boot, so most callers here win on an already-signaled welcome). No orphaned task or
            // registration survives this call once it returns, for either outcome.
            try
            {
                return Result.Success(await welcomeTask.WaitAsync(ct));
            }
            catch (OperationCanceledException)
            {
                return Result.Failure<string>(
                    "EventSub WebSocket start cancelled before welcome.",
                    "SERVICE_UNAVAILABLE"
                );
            }
        }

        public async Task StopAsync()
        {
            CancellationTokenSource? cts;
            IWebSocketChannel? channel;
            Task? loop;
            lock (_stateLock)
            {
                cts = _runCts;
                channel = _channel;
                loop = _receiveLoop;
                _runCts = null;
                _channel = null;
                _receiveLoop = null;
                // Drop the completed welcome so a later restart re-arms a fresh one instead of returning a dead id.
                _startWelcome = null;
            }

            if (cts is not null)
            {
                await cts.CancelAsync();
                cts.Dispose();
            }

            if (loop is not null)
            {
                try
                {
                    await loop;
                }
                catch (OperationCanceledException) { }
            }

            if (channel is not null)
                await channel.DisposeAsync();

            _sessionId = null;
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
                string? droppedSessionId = _sessionId;
                string reason;
                try
                {
                    connectUrl = await ConnectAndReceiveAsync(connectUrl, firstWelcome, ct);
                    backoff = TimeSpan.FromSeconds(1); // a clean reconnect-url swap resets backoff
                    continue;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (KeepaliveTimeoutException)
                {
                    reason = "keepalive timeout";
                    _logger.LogWarning(
                        "EventSub WS ({Owner}) keepalive timeout; reconnecting in {Backoff:g}",
                        _ownerKey,
                        backoff
                    );
                }
                catch (UnexpectedCloseException close)
                {
                    reason =
                        $"closed by server (code {close.CloseStatus?.ToString() ?? "unknown"} — {close.CloseStatusDescription ?? "none"})";
                    _logger.LogWarning(
                        "EventSub WS ({Owner}) {Reason}; reconnecting in {Backoff:g}",
                        _ownerKey,
                        reason,
                        backoff
                    );
                }
                catch (Exception ex)
                {
                    reason = ex.GetType().Name;
                    _logger.LogWarning(
                        "EventSub WS ({Owner}) dropped ({Reason}); reconnecting in {Backoff:g}",
                        _ownerKey,
                        reason,
                        backoff
                    );
                }

                _sessionId = null;
                if (ct.IsCancellationRequested)
                    break;

                // Exponential backoff capped at 64 s, plus full jitter, so a fleet does not thunder Twitch. The
                // pre-jitter schedule is recorded so a caller (test or diagnostics) can assert the doubling
                // sequence even though the actual delay is randomized.
                lock (_stateLock)
                    _backoffSchedule.Add(backoff);

                await _owner.ForwardDisconnectedAsync(
                    _ownerKey,
                    droppedSessionId,
                    reason,
                    backoff,
                    ct
                );

                try
                {
                    await Task.Delay(WithJitter(backoff), _clock, ct);
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
            IWebSocketChannel channel = await _channelFactory.ConnectAsync(new(url), ct);

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

                    // A Close frame here is Twitch (or the network) dropping us unexpectedly — e.g. close code
                    // 4003 "connection unused" (no subscriptions bound in time) or 4004 "reconnect grace time
                    // expired". This is NEVER a clean, planned handoff (that is the "session_reconnect" MESSAGE
                    // handled below, which returns its own reconnect_url) — treating it as one previously let
                    // RunWithReconnectAsync's catch-free return path reset backoff to 1s and immediately
                    // reconnect, tight-spinning against Twitch on a server-side close. Throwing routes it through
                    // the SAME catch(Exception) block as any other drop, so exponential backoff applies here too.
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new UnexpectedCloseException(
                            result.CloseStatus,
                            result.CloseStatusDescription
                        );

                    frame.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

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
                _logger.LogWarning(ex, "EventSub WS ({Owner}): malformed frame", _ownerKey);
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
                    await _owner.ForwardWelcomeAsync(_ownerKey, sessionId, ct);
                    break;
                }

                case "session_keepalive":
                    // Liveness only; the receive loop's keepalive timer already tracks silence.
                    break;

                case "session_reconnect":
                {
                    string? reconnectUrl = envelope.Payload?.Session?.ReconnectUrl;
                    _logger.LogInformation(
                        "EventSub WS ({Owner}): server requested reconnect",
                        _ownerKey
                    );
                    return reconnectUrl ?? DefaultWsUrl;
                }

                case "revocation":
                {
                    Wire.Subscription? sub = envelope.Payload?.Subscription;
                    if (sub is not null)
                        await _owner.ForwardRevocationAsync(
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
                    Wire.Subscription? sub = envelope.Payload?.Subscription;
                    JsonElement? @event = envelope.Payload?.Event;
                    if (sub?.Type is null || @event is null)
                        break;

                    await _owner.ForwardNotificationAsync(
                        envelope.Metadata.MessageId ?? Guid.NewGuid().ToString(),
                        ParseTimestamp(envelope.Metadata.MessageTimestamp),
                        sub.Type,
                        sub.Version ?? "1",
                        ResolveTenantKey(sub, @event),
                        @event.Value,
                        ct
                    );
                    break;
                }
            }

            return null;
        }

        private TimeSpan WithJitter(TimeSpan baseDelay)
        {
            // Full jitter (AWS recipe): uniform in [0, baseDelay]. Avoids synchronized fleet reconnects.
            double seconds = baseDelay.TotalSeconds * Random.Shared.NextDouble();
            return TimeSpan.FromSeconds(seconds);
        }
    }

    /// <summary>
    /// The Twitch user id this notification belongs to US as.
    ///
    /// <para>For every topic but one, the payload answers it: the event body names the broadcaster whose
    /// channel it happened in. <c>channel.raid</c> is the exception — its payload names the raider AND the
    /// raided, and we subscribe BOTH directions, so the payload cannot say which side we are. Worse, the
    /// payload scan checks <c>to_broadcaster_user_id</c> first, so an outgoing raid would be attributed to
    /// the channel being raided — and if that channel is also hosted here, to a real, wrong tenant.</para>
    ///
    /// <para>The subscription's own condition is the authoritative answer, so raids use it.</para>
    /// </summary>
    private static string ResolveTenantKey(Wire.Subscription sub, JsonElement? @event)
    {
        if (sub is { Type: "channel.raid", Condition: { } condition })
        {
            if (
                condition.TryGetValue("from_broadcaster_user_id", out string? from)
                && !string.IsNullOrEmpty(from)
            )
                return from;
            if (
                condition.TryGetValue("to_broadcaster_user_id", out string? to)
                && !string.IsNullOrEmpty(to)
            )
                return to;
        }

        return ExtractBroadcasterId(@event);
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
    // names the subscribed user as user_id — the broadcaster themselves, so it resolves per channel — and
    // user.whisper.message names the whisper's recipient as to_user_id, the bot's own account. A dedicated bot
    // shared across channels is not a channel, so that id resolves to no tenant; the sink attributes those
    // whispers to the platform sentinel (Guid.Empty) rather than dropping them or guessing a channel.
    private static readonly string[] BroadcasterIdKeys =
    [
        "broadcaster_user_id",
        "to_broadcaster_user_id",
        "user_id",
        "to_user_id",
    ];

    private static DateTimeOffset ParseTimestamp(string? raw) =>
        DateTimeOffset.TryParse(raw, out DateTimeOffset parsed) ? parsed : DateTimeOffset.UtcNow;

    private sealed class KeepaliveTimeoutException : Exception;

    /// <summary>
    /// A server- or network-initiated Close frame (e.g. Twitch close code 4003/4004/4005/4006/4007) — always an
    /// unplanned drop, distinct from the planned "session_reconnect" MESSAGE (which carries its own
    /// reconnect_url and never throws). Routes through the exponential-backoff catch path instead of the
    /// zero-delay "clean URL swap" return.
    /// </summary>
    private sealed class UnexpectedCloseException(
        WebSocketCloseStatus? closeStatus,
        string? closeStatusDescription
    ) : Exception
    {
        public WebSocketCloseStatus? CloseStatus { get; } = closeStatus;
        public string? CloseStatusDescription { get; } = closeStatusDescription;
    }

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

            /// <summary>
            /// The condition Twitch echoes back with every notification. Needed for raids: one
            /// channel.raid payload names BOTH sides, so the payload alone cannot say which of them we
            /// subscribed as — and on an instance hosting both channels, guessing picks the wrong tenant.
            /// </summary>
            public Dictionary<string, string>? Condition { get; init; }
        }
    }
}
