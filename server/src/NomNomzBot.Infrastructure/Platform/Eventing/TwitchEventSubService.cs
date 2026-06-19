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
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Eventing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Manages the Twitch EventSub WebSocket connection.
///
/// Flow:
///   1. Connect to wss://eventsub.wss.twitch.tv/ws
///   2. Receive session_welcome → extract session_id
///   3. Create subscriptions via REST POST /eventsub/subscriptions
///   4. Receive notification messages → publish domain events to IEventBus
///   5. Handle session_reconnect: connect to new URL, wait for Welcome, then close old
///
/// ITwitchEventSubService methods let callers subscribe/unsubscribe at runtime.
/// Reconnects automatically with exponential back-off capped at 64 s.
/// </summary>
public sealed class TwitchEventSubService : ITwitchEventSubService, IHostedService
{
    private const string DefaultWsUrl =
        "wss://eventsub.wss.twitch.tv/ws?keepalive_timeout_seconds=30";
    private const string HelixBase = "https://api.twitch.tv/helix";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _eventBus;
    private readonly TwitchOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<TwitchEventSubService> _logger;
    private readonly TimeProvider _timeProvider;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    private string? _sessionId;

    // Pending subscriptions: broadcasterId → list of eventType
    // Stored before the WebSocket connects, then subscribed once session_id is available
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _pendingSubscriptions =
        new();

    // Active subscriptions: subscriptionId → (broadcasterId, eventType)
    private readonly ConcurrentDictionary<
        string,
        (string BroadcasterId, string EventType)
    > _activeSubscriptions = new();

    // ── Broadcaster-specific WebSocket sessions (one per broadcaster) ──────────

    private sealed class BroadcasterSession
    {
        public string? SessionId { get; set; }
        public readonly ConcurrentBag<string> PendingSubscriptions = new();
        public readonly CancellationTokenSource Cts = new();
        public Task? ReceiveLoop { get; set; }
    }

    private readonly ConcurrentDictionary<string, BroadcasterSession> _broadcasterSessions = new();

    public TwitchEventSubService(
        IServiceScopeFactory scopeFactory,
        IEventBus eventBus,
        IOptions<TwitchOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<TwitchEventSubService> logger,
        TimeProvider timeProvider
    )
    {
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _options = options.Value;
        _http = httpClientFactory.CreateClient("twitch-eventsub");
        _logger = logger;
        _timeProvider = timeProvider;
    }

    // ─── IHostedService ───────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveLoop = Task.Run(() => RunWithReconnectAsync(_cts.Token), _cts.Token);
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop;
            }
            catch (OperationCanceledException) { }
        }

        // Cancel all broadcaster sessions
        foreach (BroadcasterSession session in _broadcasterSessions.Values)
        {
            await session.Cts.CancelAsync();
        }
    }

    // ─── ITwitchEventSubService ───────────────────────────────────────────────────

    public async Task SubscribeAsync(
        string broadcasterId,
        string eventType,
        CancellationToken ct = default
    )
    {
        if (BroadcasterScopedEvents.Contains(eventType))
        {
            await SubscribeOnBroadcasterSessionAsync(broadcasterId, eventType, ct);
            return;
        }

        if (_sessionId is null)
        {
            // Queue for when the bot session is ready
            _pendingSubscriptions.GetOrAdd(broadcasterId, _ => new()).Add(eventType);

            _logger.LogDebug(
                "EventSub: Queued subscription {EventType} for {BroadcasterId} (no session yet)",
                eventType,
                broadcasterId
            );
            return;
        }

        await CreateSubscriptionAsync(broadcasterId, eventType, _sessionId, ct);
    }

    private async Task SubscribeOnBroadcasterSessionAsync(
        string broadcasterId,
        string eventType,
        CancellationToken ct
    )
    {
        BroadcasterSession session = _broadcasterSessions.GetOrAdd(broadcasterId, _ => new());

        if (session.SessionId is null)
        {
            session.PendingSubscriptions.Add(eventType);

            // Start the broadcaster WebSocket if not already running
            if (session.ReceiveLoop is null || session.ReceiveLoop.IsCompleted)
            {
                session.ReceiveLoop = Task.Run(
                    () => RunBroadcasterSessionAsync(broadcasterId, session, ct),
                    ct
                );
            }

            _logger.LogDebug(
                "EventSub: Queued broadcaster-scoped {EventType} for {BroadcasterId} (no broadcaster session yet)",
                eventType,
                broadcasterId
            );
            return;
        }

        await CreateSubscriptionWithTokenAsync(
            broadcasterId,
            eventType,
            session.SessionId,
            await GetBroadcasterTokenAsync(broadcasterId, ct),
            ct
        );
    }

    private async Task RunBroadcasterSessionAsync(
        string broadcasterId,
        BroadcasterSession session,
        CancellationToken parentCt
    )
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            parentCt,
            session.Cts.Token
        );
        CancellationToken ct = linked.Token;

        TimeSpan delay = TimeSpan.FromSeconds(2);
        string connectUrl = DefaultWsUrl;

        while (!ct.IsCancellationRequested)
        {
            string? token = await GetBroadcasterTokenAsync(broadcasterId, ct);
            if (token is null)
            {
                _logger.LogWarning(
                    "EventSub: No broadcaster token for {BroadcasterId}, skipping broadcaster session",
                    broadcasterId
                );
                return;
            }

            using ClientWebSocket ws = new();
            session.SessionId = null;

            try
            {
                _logger.LogInformation(
                    "EventSub: Connecting broadcaster session for {BroadcasterId}",
                    broadcasterId
                );
                await ws.ConnectAsync(new Uri(connectUrl), ct);
                _logger.LogInformation(
                    "EventSub: Broadcaster session connected for {BroadcasterId}",
                    broadcasterId
                );

                connectUrl = await ReceiveBroadcasterLoopAsync(
                    ws,
                    broadcasterId,
                    session,
                    token,
                    ct
                );
                delay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EventSub: Broadcaster session for {BroadcasterId} dropped, reconnecting in {Delay:g}",
                    broadcasterId,
                    delay
                );
            }

            if (ct.IsCancellationRequested)
                break;
            await Task.Delay(delay, ct);
            delay = TimeSpan.FromSeconds(Math.Min((int)delay.TotalSeconds * 2, 64));
            connectUrl = DefaultWsUrl;
        }
    }

    private async Task<string> ReceiveBroadcasterLoopAsync(
        ClientWebSocket ws,
        string broadcasterId,
        BroadcasterSession session,
        string token,
        CancellationToken ct
    )
    {
        byte[] buffer = new byte[64 * 1024];

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            EventSubEnvelope? envelope = JsonSerializer.Deserialize<EventSubEnvelope>(
                json,
                JsonOptions
            );
            if (envelope?.Metadata is null)
                continue;

            switch (envelope.Metadata.MessageType)
            {
                case "session_welcome":
                {
                    string? sessionId = envelope.Payload?.Session?.Id;
                    if (sessionId is null)
                        break;

                    session.SessionId = sessionId;
                    _logger.LogInformation(
                        "EventSub: Broadcaster session established for {BroadcasterId} (session={SessionId})",
                        broadcasterId,
                        sessionId
                    );

                    // Subscribe all pending
                    foreach (string eventType in session.PendingSubscriptions.ToArray())
                    {
                        await CreateSubscriptionWithTokenAsync(
                            broadcasterId,
                            eventType,
                            sessionId,
                            token,
                            ct
                        );
                    }
                    break;
                }

                case "session_reconnect":
                {
                    string? reconnectUrl = envelope.Payload?.Session?.ReconnectUrl;
                    if (reconnectUrl is not null)
                        return reconnectUrl;
                    break;
                }

                case "notification":
                    await HandleNotificationAsync(envelope, ct);
                    break;

                case "session_keepalive":
                    break;

                case "revocation":
                    _logger.LogWarning(
                        "EventSub: Broadcaster subscription revoked for {BroadcasterId}",
                        broadcasterId
                    );
                    break;
            }
        }

        return DefaultWsUrl;
    }

    private async Task CreateSubscriptionWithTokenAsync(
        string broadcasterId,
        string eventType,
        string sessionId,
        string? token,
        CancellationToken ct
    )
    {
        if (token is null)
        {
            _logger.LogWarning(
                "EventSub: No token for broadcaster {BroadcasterId}, cannot subscribe {EventType}",
                broadcasterId,
                eventType
            );
            return;
        }

        Dictionary<string, string> condition = BuildCondition(broadcasterId, eventType);
        string version = GetSubscriptionVersion(eventType);

        var body = new
        {
            type = eventType,
            version,
            condition,
            transport = new { method = "websocket", session_id = sessionId },
        };

        HttpRequestMessage request = new(HttpMethod.Post, $"{HelixBase}/eventsub/subscriptions");
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Content = JsonContent.Create(body);

        HttpResponseMessage resp = await _http.SendAsync(request, ct);
        if (!resp.IsSuccessStatusCode)
        {
            string err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "EventSub: Failed to subscribe {EventType} for {BroadcasterId}: {Status} — {Error}",
                eventType,
                broadcasterId,
                resp.StatusCode,
                err
            );
            return;
        }

        HelixDataResponse<EventSubSubscription>? result = await resp.Content.ReadFromJsonAsync<
            HelixDataResponse<EventSubSubscription>
        >(cancellationToken: ct);
        EventSubSubscription? sub = result?.Data?.FirstOrDefault();
        if (sub is not null)
        {
            _activeSubscriptions[sub.Id] = (broadcasterId, eventType);
            _logger.LogInformation(
                "EventSub: Subscribed {EventType} for {BroadcasterId} (id={SubId})",
                eventType,
                broadcasterId,
                sub.Id
            );
        }
    }

    public async Task UnsubscribeAsync(string subscriptionId, CancellationToken ct = default)
    {
        string? token = await GetBotTokenAsync(ct);
        if (token is null)
            return;

        HttpRequestMessage request = new(
            HttpMethod.Delete,
            $"{HelixBase}/eventsub/subscriptions?id={Uri.EscapeDataString(subscriptionId)}"
        );
        request.Headers.Add("Authorization", $"Bearer {token}");

        HttpResponseMessage resp = await _http.SendAsync(request, ct);

        if (resp.IsSuccessStatusCode)
        {
            _activeSubscriptions.TryRemove(subscriptionId, out _);
            _logger.LogInformation("EventSub: Unsubscribed {SubscriptionId}", subscriptionId);
        }
        else
        {
            _logger.LogWarning(
                "EventSub: Failed to unsubscribe {SubscriptionId}: {Status}",
                subscriptionId,
                resp.StatusCode
            );
        }
    }

    public async Task<IReadOnlyList<string>> GetActiveSubscriptionsAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        await Task.CompletedTask;
        return _activeSubscriptions
            .Where(kvp => kvp.Value.BroadcasterId == broadcasterId)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    // ─── Connection loop ──────────────────────────────────────────────────────────

    private async Task RunWithReconnectAsync(CancellationToken ct)
    {
        TimeSpan delay = TimeSpan.FromSeconds(1);
        string connectUrl = DefaultWsUrl;

        while (!ct.IsCancellationRequested)
        {
            bool cleanClose = false;
            try
            {
                connectUrl = await ConnectAndReceiveAsync(connectUrl, ct);
                cleanClose = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EventSub connection dropped, reconnecting in {Delay:g}",
                    delay
                );
                connectUrl = DefaultWsUrl; // reset to default on error
            }

            if (ct.IsCancellationRequested)
                break;

            _sessionId = null;

            // If the session closed cleanly with no active subscriptions (e.g. no channels
            // configured yet), back off longer rather than tight-looping.
            if (cleanClose && _activeSubscriptions.IsEmpty)
            {
                TimeSpan idleDelay = TimeSpan.FromMinutes(5);
                _logger.LogInformation(
                    "EventSub: No subscriptions — waiting {Delay:g} before reconnect",
                    idleDelay
                );
                await Task.Delay(idleDelay, ct);
                delay = TimeSpan.FromSeconds(1); // reset on next real connect
            }
            else
            {
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 64));
            }
        }
    }

    /// <summary>
    /// Returns the next URL to connect to (either default or reconnect URL).
    /// </summary>
    private async Task<string> ConnectAndReceiveAsync(string url, CancellationToken ct)
    {
        _ws?.Dispose();
        _ws = new();

        _logger.LogInformation("EventSub: Connecting to {Url}", url);
        await _ws.ConnectAsync(new(url), ct);
        _logger.LogInformation("EventSub: Connected");

        byte[] buffer = new byte[16384];
        StringBuilder sb = new();

        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            sb.Clear();
            WebSocketReceiveResult result;

            do
            {
                result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return DefaultWsUrl;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            string? reconnectUrl = await HandleEventSubMessageAsync(sb.ToString(), ct);
            if (reconnectUrl is not null)
                return reconnectUrl;
        }

        return DefaultWsUrl;
    }

    private async Task<string?> HandleEventSubMessageAsync(string json, CancellationToken ct)
    {
        EventSubEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventSubEnvelope>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventSub: Failed to parse message");
            return null;
        }

        if (envelope is null)
            return null;

        switch (envelope.Metadata?.MessageType)
        {
            case "session_welcome":
                _sessionId = envelope.Payload?.Session?.Id;
                _logger.LogInformation("EventSub: Session established {SessionId}", _sessionId);

                if (_sessionId is not null)
                    await SubscribePendingAsync(_sessionId, ct);
                break;

            case "session_keepalive":
                // No-op — confirms connection is alive
                break;

            case "notification":
                await HandleNotificationAsync(envelope, ct);
                break;

            case "session_reconnect":
                string? reconnectUrl = envelope.Payload?.Session?.ReconnectUrl;
                _logger.LogWarning("EventSub: Server requested reconnect to {Url}", reconnectUrl);
                return reconnectUrl ?? DefaultWsUrl;

            case "revocation":
                string? subId = envelope.Payload?.Subscription?.Id;
                if (subId is not null)
                {
                    _activeSubscriptions.TryRemove(subId, out _);
                    _logger.LogWarning("EventSub: Subscription {SubId} revoked", subId);
                }
                break;
        }

        return null;
    }

    // ─── Notification dispatch ────────────────────────────────────────────────────

    private async Task HandleNotificationAsync(EventSubEnvelope envelope, CancellationToken ct)
    {
        string? subscriptionType = envelope.Payload?.Subscription?.Type;
        JsonElement? eventData = envelope.Payload?.Event;
        string broadcasterId =
            eventData?.GetProp("broadcaster_user_id")
            ?? eventData?.GetProp("to_broadcaster_user_id")
            ?? string.Empty;

        _logger.LogDebug(
            "EventSub notification: {Type} for {BroadcasterId}",
            subscriptionType,
            broadcasterId
        );

        switch (subscriptionType)
        {
            case "channel.follow":
                await _eventBus.PublishAsync(
                    new FollowEvent
                    {
                        BroadcasterId = broadcasterId,
                        UserId = eventData?.GetProp("user_id") ?? string.Empty,
                        UserLogin = eventData?.GetProp("user_login") ?? string.Empty,
                        UserDisplayName = eventData?.GetProp("user_name") ?? string.Empty,
                        FollowedAt = DateTimeOffset.TryParse(
                            eventData?.GetProp("followed_at"),
                            out DateTimeOffset fa
                        )
                            ? fa
                            : _timeProvider.GetUtcNow(),
                    },
                    ct
                );
                break;

            case "channel.subscribe":
                await _eventBus.PublishAsync(
                    new NewSubscriptionEvent
                    {
                        BroadcasterId = broadcasterId,
                        UserId = eventData?.GetProp("user_id") ?? string.Empty,
                        UserDisplayName = eventData?.GetProp("user_name") ?? string.Empty,
                        Tier = eventData?.GetProp("tier") ?? "1000",
                    },
                    ct
                );
                break;

            case "channel.subscription.gift":
                int.TryParse(eventData?.GetProp("total"), out int giftCount);
                bool isAnon = eventData?.GetProp("is_anonymous") == "true";

                await _eventBus.PublishAsync(
                    new GiftSubscriptionEvent
                    {
                        BroadcasterId = broadcasterId,
                        GifterUserId = eventData?.GetProp("user_id") ?? string.Empty,
                        GifterDisplayName = eventData?.GetProp("user_name") ?? string.Empty,
                        Tier = eventData?.GetProp("tier") ?? "1000",
                        GiftCount = Math.Max(giftCount, 1),
                        IsAnonymous = isAnon,
                        Recipients = [],
                    },
                    ct
                );
                break;

            case "channel.cheer":
                int.TryParse(eventData?.GetProp("bits"), out int bits);
                await _eventBus.PublishAsync(
                    new CheerEvent
                    {
                        BroadcasterId = broadcasterId,
                        UserId = eventData?.GetProp("user_id") ?? string.Empty,
                        UserDisplayName = eventData?.GetProp("user_name") ?? string.Empty,
                        Bits = bits,
                        Message = eventData?.GetProp("message") ?? string.Empty,
                        IsAnonymous = eventData?.GetProp("is_anonymous") == "true",
                    },
                    ct
                );
                break;

            case "channel.raid":
                int.TryParse(eventData?.GetProp("viewers"), out int viewers);
                await _eventBus.PublishAsync(
                    new RaidEvent
                    {
                        BroadcasterId = broadcasterId,
                        FromUserId = eventData?.GetProp("from_broadcaster_user_id") ?? string.Empty,
                        FromDisplayName =
                            eventData?.GetProp("from_broadcaster_user_name") ?? string.Empty,
                        FromLogin =
                            eventData?.GetProp("from_broadcaster_user_login") ?? string.Empty,
                        ViewerCount = viewers,
                    },
                    ct
                );
                break;

            case "channel.ban":
                if (eventData.HasValue)
                {
                    bool isPermanent = eventData.Value.GetProp("is_permanent") == "true";
                    string? banDurationStr = eventData.Value.GetProp("ban_duration_seconds");
                    int.TryParse(banDurationStr, out int banDuration);
                    bool hasDuration = !isPermanent && banDuration > 0;

                    if (hasDuration)
                    {
                        await _eventBus.PublishAsync(
                            new UserTimedOutEvent
                            {
                                BroadcasterId = broadcasterId,
                                TargetUserId = eventData.Value.GetProp("user_id") ?? string.Empty,
                                TargetDisplayName =
                                    eventData.Value.GetProp("user_name") ?? string.Empty,
                                ModeratorUserId =
                                    eventData.Value.GetProp("moderator_user_id") ?? string.Empty,
                                DurationSeconds = banDuration,
                                Reason = eventData.Value.GetProp("reason"),
                            },
                            ct
                        );
                    }
                    else
                    {
                        await _eventBus.PublishAsync(
                            new UserBannedEvent
                            {
                                BroadcasterId = broadcasterId,
                                TargetUserId = eventData.Value.GetProp("user_id") ?? string.Empty,
                                TargetDisplayName =
                                    eventData.Value.GetProp("user_name") ?? string.Empty,
                                ModeratorUserId =
                                    eventData.Value.GetProp("moderator_user_id") ?? string.Empty,
                                Reason = eventData.Value.GetProp("reason"),
                            },
                            ct
                        );
                    }
                }
                break;

            case "channel.update":
                await _eventBus.PublishAsync(
                    new ChannelUpdatedEvent
                    {
                        BroadcasterId = broadcasterId,
                        BroadcasterDisplayName =
                            eventData?.GetProp("broadcaster_user_name") ?? broadcasterId,
                        NewTitle = eventData?.GetProp("title") ?? string.Empty,
                        NewGameName = eventData?.GetProp("category_name") ?? string.Empty,
                    },
                    ct
                );
                break;

            case "channel.shoutout.create":
                await _eventBus.PublishAsync(
                    new ShoutoutSentEvent
                    {
                        BroadcasterId = broadcasterId,
                        ToUserId = eventData?.GetProp("to_broadcaster_user_id") ?? string.Empty,
                        ToDisplayName =
                            eventData?.GetProp("to_broadcaster_user_name") ?? string.Empty,
                    },
                    ct
                );
                break;

            case "channel.channel_points_custom_reward_redemption.add":
                JsonElement? reward =
                    envelope.Payload?.Event?.TryGetProperty("reward", out JsonElement rewardProp)
                    == true
                        ? rewardProp
                        : (JsonElement?)null;

                await _eventBus.PublishAsync(
                    new RewardRedeemedEvent
                    {
                        BroadcasterId = broadcasterId,
                        RedemptionId = eventData?.GetProp("id") ?? string.Empty,
                        RewardId = reward?.GetProp("id") ?? string.Empty,
                        RewardTitle = reward?.GetProp("title") ?? string.Empty,
                        Cost =
                            reward?.TryGetProperty("cost", out JsonElement costProp) == true
                                ? costProp.GetInt32()
                                : 0,
                        UserId = eventData?.GetProp("user_id") ?? string.Empty,
                        UserDisplayName = eventData?.GetProp("user_name") ?? string.Empty,
                        UserInput = eventData?.GetProp("user_input"),
                    },
                    ct
                );
                break;

            case "stream.online":
                string streamTitle = eventData?.GetProp("title") ?? string.Empty;
                string gameName = eventData?.GetProp("category_name") ?? string.Empty;
                DateTimeOffset.TryParse(
                    eventData?.GetProp("started_at"),
                    out DateTimeOffset startedAt
                );

                await _eventBus.PublishAsync(
                    new ChannelOnlineEvent
                    {
                        BroadcasterId = broadcasterId,
                        BroadcasterDisplayName =
                            eventData?.GetProp("broadcaster_user_name") ?? broadcasterId,
                        StreamTitle = streamTitle,
                        GameName = gameName,
                        StartedAt = startedAt == default ? _timeProvider.GetUtcNow() : startedAt,
                    },
                    ct
                );
                break;

            case "stream.offline":
                await _eventBus.PublishAsync(
                    new ChannelOfflineEvent
                    {
                        BroadcasterId = broadcasterId,
                        BroadcasterDisplayName =
                            eventData?.GetProp("broadcaster_user_name") ?? broadcasterId,
                        StreamDuration = TimeSpan.Zero, // Duration requires tracking stream start externally
                    },
                    ct
                );
                break;

            case "channel.channel_points_custom_reward.add":
                await _eventBus.PublishAsync(
                    new RewardCreatedEvent
                    {
                        BroadcasterId = broadcasterId,
                        TwitchRewardId = eventData?.GetProp("id") ?? string.Empty,
                        Title = eventData?.GetProp("title") ?? string.Empty,
                        Cost =
                            envelope.Payload?.Event?.TryGetProperty("cost", out JsonElement cp)
                            == true
                                ? cp.GetInt32()
                                : 0,
                        IsEnabled = eventData?.GetProp("is_enabled") != "false",
                    },
                    ct
                );
                break;

            case "channel.channel_points_custom_reward.update":
                await _eventBus.PublishAsync(
                    new RewardUpdatedEvent
                    {
                        BroadcasterId = broadcasterId,
                        TwitchRewardId = eventData?.GetProp("id") ?? string.Empty,
                        Title = eventData?.GetProp("title") ?? string.Empty,
                        Cost =
                            envelope.Payload?.Event?.TryGetProperty("cost", out JsonElement cup)
                            == true
                                ? cup.GetInt32()
                                : 0,
                        IsEnabled = eventData?.GetProp("is_enabled") != "false",
                    },
                    ct
                );
                break;

            case "channel.channel_points_custom_reward.remove":
                await _eventBus.PublishAsync(
                    new RewardRemovedEvent
                    {
                        BroadcasterId = broadcasterId,
                        TwitchRewardId = eventData?.GetProp("id") ?? string.Empty,
                        Title = eventData?.GetProp("title") ?? string.Empty,
                    },
                    ct
                );
                break;

            case "channel.poll.begin":
                if (eventData.HasValue)
                    await HandlePollBeginAsync(eventData.Value, broadcasterId, ct);
                break;

            case "channel.poll.end":
                if (eventData.HasValue)
                    await HandlePollEndAsync(eventData.Value, broadcasterId, ct);
                break;

            case "channel.prediction.begin":
                if (eventData.HasValue)
                    await HandlePredictionBeginAsync(eventData.Value, broadcasterId, ct);
                break;

            case "channel.prediction.lock":
                if (eventData.HasValue)
                    await HandlePredictionLockAsync(eventData.Value, broadcasterId, ct);
                break;

            case "channel.prediction.end":
                if (eventData.HasValue)
                    await HandlePredictionEndAsync(eventData.Value, broadcasterId, ct);
                break;

            case "channel.hype_train.begin":
                int.TryParse(eventData?.GetProp("level"), out int htLevel);
                int.TryParse(eventData?.GetProp("total"), out int htTotal);
                int.TryParse(eventData?.GetProp("goal"), out int htGoal);
                DateTimeOffset.TryParse(
                    eventData?.GetProp("expires_at"),
                    out DateTimeOffset htExpires
                );
                await _eventBus.PublishAsync(
                    new HypeTrainBeganEvent
                    {
                        BroadcasterId = broadcasterId,
                        HypeTrainId = eventData?.GetProp("id") ?? string.Empty,
                        Level = htLevel,
                        Total = htTotal,
                        Goal = htGoal,
                        ExpiresAt =
                            htExpires == default
                                ? _timeProvider.GetUtcNow().AddMinutes(5)
                                : htExpires,
                    },
                    ct
                );
                break;

            case "channel.hype_train.end":
                int.TryParse(eventData?.GetProp("level"), out int hteLevel);
                int.TryParse(eventData?.GetProp("total"), out int hteTotal);
                await _eventBus.PublishAsync(
                    new HypeTrainEndedEvent
                    {
                        BroadcasterId = broadcasterId,
                        HypeTrainId = eventData?.GetProp("id") ?? string.Empty,
                        Level = hteLevel,
                        Total = hteTotal,
                    },
                    ct
                );
                break;

            case "channel.chat.message":
                if (eventData.HasValue)
                    await HandleChatMessageAsync(eventData.Value, broadcasterId, ct);
                break;

            case "channel.chat.message_delete":
                if (eventData.HasValue)
                    await _eventBus.PublishAsync(
                        new ChatMessageDeletedEvent
                        {
                            BroadcasterId = broadcasterId,
                            MessageId = eventData.Value.GetProp("message_id") ?? string.Empty,
                            DeletedByUserId =
                                eventData.Value.GetProp("moderator_user_id") ?? string.Empty,
                            TargetUserId =
                                eventData.Value.GetProp("target_user_id") ?? string.Empty,
                        },
                        ct
                    );
                break;
        }
    }

    // ─── Poll / Prediction / HypeTrain parsing ────────────────────────────────────

    private async Task HandlePollBeginAsync(
        JsonElement ev,
        string broadcasterId,
        CancellationToken ct
    )
    {
        IReadOnlyList<PollChoice> choices = ParsePollChoices(ev);
        int.TryParse(ev.GetProp("duration_seconds"), out int duration);
        DateTimeOffset.TryParse(ev.GetProp("ends_at"), out DateTimeOffset endsAt);

        await _eventBus.PublishAsync(
            new PollBeganEvent
            {
                BroadcasterId = broadcasterId,
                PollId = ev.GetProp("id") ?? string.Empty,
                Title = ev.GetProp("title") ?? string.Empty,
                Choices = choices,
                DurationSeconds = duration,
                EndsAt =
                    endsAt == default ? _timeProvider.GetUtcNow().AddSeconds(duration) : endsAt,
            },
            ct
        );
    }

    private async Task HandlePollEndAsync(
        JsonElement ev,
        string broadcasterId,
        CancellationToken ct
    )
    {
        IReadOnlyList<PollChoice> choices = ParsePollChoices(ev);
        await _eventBus.PublishAsync(
            new PollEndedEvent
            {
                BroadcasterId = broadcasterId,
                PollId = ev.GetProp("id") ?? string.Empty,
                Title = ev.GetProp("title") ?? string.Empty,
                Status = ev.GetProp("status") ?? string.Empty,
                Choices = choices,
            },
            ct
        );
    }

    private static IReadOnlyList<PollChoice> ParsePollChoices(JsonElement ev)
    {
        List<PollChoice> choices = new();
        if (
            ev.TryGetProperty("choices", out JsonElement choicesArr)
            && choicesArr.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement c in choicesArr.EnumerateArray())
            {
                c.TryGetProperty("votes", out JsonElement votesEl);
                c.TryGetProperty("channel_points_votes", out JsonElement cpVotesEl);
                choices.Add(
                    new(
                        c.GetProp("id") ?? string.Empty,
                        c.GetProp("title") ?? string.Empty,
                        votesEl.ValueKind == JsonValueKind.Number ? votesEl.GetInt32() : 0,
                        cpVotesEl.ValueKind == JsonValueKind.Number ? cpVotesEl.GetInt32() : 0
                    )
                );
            }
        }
        return choices;
    }

    private async Task HandlePredictionBeginAsync(
        JsonElement ev,
        string broadcasterId,
        CancellationToken ct
    )
    {
        IReadOnlyList<PredictionOutcome> outcomes = ParsePredictionOutcomes(ev);
        int.TryParse(ev.GetProp("prediction_window"), out int window);
        DateTimeOffset.TryParse(ev.GetProp("locks_at"), out DateTimeOffset locksAt);

        await _eventBus.PublishAsync(
            new PredictionBeganEvent
            {
                BroadcasterId = broadcasterId,
                PredictionId = ev.GetProp("id") ?? string.Empty,
                Title = ev.GetProp("title") ?? string.Empty,
                Outcomes = outcomes,
                WindowSeconds = window,
                LocksAt =
                    locksAt == default ? _timeProvider.GetUtcNow().AddSeconds(window) : locksAt,
            },
            ct
        );
    }

    private async Task HandlePredictionLockAsync(
        JsonElement ev,
        string broadcasterId,
        CancellationToken ct
    )
    {
        IReadOnlyList<PredictionOutcome> outcomes = ParsePredictionOutcomes(ev);
        await _eventBus.PublishAsync(
            new PredictionLockedEvent
            {
                BroadcasterId = broadcasterId,
                PredictionId = ev.GetProp("id") ?? string.Empty,
                Title = ev.GetProp("title") ?? string.Empty,
                Outcomes = outcomes,
            },
            ct
        );
    }

    private async Task HandlePredictionEndAsync(
        JsonElement ev,
        string broadcasterId,
        CancellationToken ct
    )
    {
        IReadOnlyList<PredictionOutcome> outcomes = ParsePredictionOutcomes(ev);
        await _eventBus.PublishAsync(
            new PredictionEndedEvent
            {
                BroadcasterId = broadcasterId,
                PredictionId = ev.GetProp("id") ?? string.Empty,
                Title = ev.GetProp("title") ?? string.Empty,
                Status = ev.GetProp("status") ?? string.Empty,
                Outcomes = outcomes,
                WinningOutcomeId = ev.GetProp("winning_outcome_id"),
            },
            ct
        );
    }

    private static IReadOnlyList<PredictionOutcome> ParsePredictionOutcomes(JsonElement ev)
    {
        List<PredictionOutcome> outcomes = new();
        if (
            ev.TryGetProperty("outcomes", out JsonElement outcomesArr)
            && outcomesArr.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement o in outcomesArr.EnumerateArray())
            {
                o.TryGetProperty("channel_points", out JsonElement cpEl);
                o.TryGetProperty("users", out JsonElement usersEl);
                outcomes.Add(
                    new(
                        o.GetProp("id") ?? string.Empty,
                        o.GetProp("title") ?? string.Empty,
                        cpEl.ValueKind == JsonValueKind.Number ? cpEl.GetInt32() : 0,
                        usersEl.ValueKind == JsonValueKind.Number ? usersEl.GetInt32() : 0,
                        o.GetProp("color") ?? string.Empty
                    )
                );
            }
        }
        return outcomes;
    }

    // ─── channel.chat.message parsing ────────────────────────────────────────────

    private async Task HandleChatMessageAsync(
        JsonElement eventData,
        string broadcasterId,
        CancellationToken ct
    )
    {
        string messageId = eventData.GetProp("message_id") ?? Guid.NewGuid().ToString();
        string userId = eventData.GetProp("chatter_user_id") ?? string.Empty;
        string userLogin = eventData.GetProp("chatter_user_login") ?? string.Empty;
        string userDisplayName = eventData.GetProp("chatter_user_name") ?? userLogin;
        string? colorHex = eventData.GetProp("color");
        string messageType = eventData.GetProp("message_type") ?? "text";

        // Parse message object
        string rawText = string.Empty;
        List<ChatMessageFragment> fragments = [];

        if (eventData.TryGetProperty("message", out JsonElement messageObj))
        {
            rawText = messageObj.GetProp("text") ?? string.Empty;

            if (
                messageObj.TryGetProperty("fragments", out JsonElement fragmentsArr)
                && fragmentsArr.ValueKind == JsonValueKind.Array
            )
            {
                foreach (JsonElement frag in fragmentsArr.EnumerateArray())
                {
                    fragments.Add(ParseFragment(frag));
                }
            }
        }

        // Parse badges
        List<ChatBadge> badges = [];
        if (
            eventData.TryGetProperty("badges", out JsonElement badgesArr)
            && badgesArr.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement badge in badgesArr.EnumerateArray())
            {
                string setId = badge.GetProp("set_id") ?? string.Empty;
                string badgeId = badge.GetProp("id") ?? string.Empty;
                string? info = badge.GetProp("info");
                badges.Add(new(setId, badgeId, info));
            }
        }

        // Parse cheer bits
        int bits = 0;
        if (
            eventData.TryGetProperty("cheer", out JsonElement cheerObj)
            && cheerObj.TryGetProperty("bits", out JsonElement bitsEl)
        )
        {
            bits = bitsEl.GetInt32();
        }

        // Parse reply thread
        string? replyParentId = null;
        string? replyParentBody = null;
        string? replyParentUserName = null;

        if (eventData.TryGetProperty("reply", out JsonElement replyObj))
        {
            replyParentId = replyObj.GetProp("parent_message_id");
            replyParentBody = replyObj.GetProp("parent_message_body");
            replyParentUserName = replyObj.GetProp("parent_user_name");
        }

        // Derive roles from badges
        bool isBroadcaster = badges.Any(b => b.SetId == "broadcaster");
        bool isModerator = isBroadcaster || badges.Any(b => b.SetId == "moderator");
        bool isVip = badges.Any(b => b.SetId == "vip");
        bool isSubscriber = badges.Any(b => b.SetId is "subscriber" or "founder");

        await _eventBus.PublishAsync(
            new ChatMessageReceivedEvent
            {
                MessageId = messageId,
                BroadcasterId = broadcasterId,
                UserId = userId,
                UserLogin = userLogin,
                UserDisplayName = userDisplayName,
                Message = rawText,
                Fragments = fragments,
                ColorHex = colorHex,
                MessageType = messageType,
                Badges = badges,
                Bits = bits,
                IsSubscriber = isSubscriber,
                IsVip = isVip,
                IsModerator = isModerator,
                IsBroadcaster = isBroadcaster,
                ReplyParentMessageId = replyParentId,
                ReplyParentMessageBody = replyParentBody,
                ReplyParentUserName = replyParentUserName,
            },
            ct
        );
    }

    private static ChatMessageFragment ParseFragment(JsonElement frag)
    {
        string type = frag.GetProp("type") ?? "text";
        string text = frag.GetProp("text") ?? string.Empty;

        switch (type)
        {
            case "emote":
            {
                if (!frag.TryGetProperty("emote", out JsonElement emoteObj))
                    return new() { Type = type, Text = text };

                string[] formats = Array.Empty<string>();
                if (
                    emoteObj.TryGetProperty("format", out JsonElement fmtArr)
                    && fmtArr.ValueKind == JsonValueKind.Array
                )
                {
                    formats = fmtArr
                        .EnumerateArray()
                        .Select(e => e.GetString() ?? string.Empty)
                        .ToArray();
                }

                return new()
                {
                    Type = type,
                    Text = text,
                    EmoteId = emoteObj.GetProp("id"),
                    EmoteSetId = emoteObj.GetProp("emote_set_id"),
                    EmoteOwnerId = emoteObj.GetProp("owner_id"),
                    EmoteFormats = formats,
                };
            }

            case "cheermote":
            {
                if (!frag.TryGetProperty("cheermote", out JsonElement cheerObj))
                    return new() { Type = type, Text = text };

                int bits = 0;
                if (cheerObj.TryGetProperty("bits", out JsonElement bitsEl))
                    bits = bitsEl.GetInt32();

                int tier = 1;
                if (cheerObj.TryGetProperty("tier", out JsonElement tierEl))
                    tier = tierEl.GetInt32();

                return new()
                {
                    Type = type,
                    Text = text,
                    CheermotePrefix = cheerObj.GetProp("prefix"),
                    CheermoteBits = bits,
                    CheermoteTier = tier,
                };
            }

            case "mention":
            {
                if (!frag.TryGetProperty("mention", out JsonElement mentionObj))
                    return new() { Type = type, Text = text };

                return new()
                {
                    Type = type,
                    Text = text,
                    MentionUserId = mentionObj.GetProp("user_id"),
                    MentionUserLogin = mentionObj.GetProp("user_login"),
                    MentionUserName = mentionObj.GetProp("user_name"),
                };
            }

            default:
                return new() { Type = type, Text = text };
        }
    }

    // ─── Subscription management ──────────────────────────────────────────────────

    private async Task SubscribePendingAsync(string sessionId, CancellationToken ct)
    {
        foreach ((string broadcasterId, ConcurrentBag<string> eventTypes) in _pendingSubscriptions)
        {
            foreach (string eventType in eventTypes)
            {
                try
                {
                    await CreateSubscriptionAsync(broadcasterId, eventType, sessionId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "EventSub: Failed to create pending subscription {EventType} for {BroadcasterId}",
                        eventType,
                        broadcasterId
                    );
                }
            }
        }

        _pendingSubscriptions.Clear();
    }

    /// <summary>
    /// Event types that require the broadcaster's own user token.
    /// The bot token does not have broadcaster-level authorization for these.
    /// </summary>
    private static readonly HashSet<string> BroadcasterScopedEvents =
    [
        "channel.follow",
        "channel.ban",
        "channel.unban",
        "channel.cheer",
        "channel.subscribe",
        "channel.subscription.gift",
        "channel.subscription.message",
        "channel.channel_points_custom_reward_redemption.add",
        "channel.channel_points_custom_reward_redemption.update",
        "channel.poll.begin",
        "channel.poll.end",
        "channel.prediction.begin",
        "channel.prediction.end",
        "channel.hype_train.begin",
        "channel.hype_train.end",
    ];

    private async Task CreateSubscriptionAsync(
        string broadcasterId,
        string eventType,
        string sessionId,
        CancellationToken ct
    )
    {
        // Bot session only handles bot-scoped events (broadcaster-scoped events are
        // handled by per-broadcaster WebSocket sessions via CreateSubscriptionWithTokenAsync).
        string? token = await GetBotTokenAsync(ct);

        if (token is null)
        {
            _logger.LogWarning(
                "EventSub: No token available, cannot subscribe {EventType}",
                eventType
            );
            return;
        }

        Dictionary<string, string> condition = BuildCondition(broadcasterId, eventType);
        string version = GetSubscriptionVersion(eventType);

        var body = new
        {
            type = eventType,
            version,
            condition,
            transport = new { method = "websocket", session_id = sessionId },
        };

        HttpRequestMessage request = new(HttpMethod.Post, $"{HelixBase}/eventsub/subscriptions");
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Content = JsonContent.Create(body);

        HttpResponseMessage resp = await _http.SendAsync(request, ct);
        if (!resp.IsSuccessStatusCode)
        {
            string err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "EventSub: Failed to subscribe {EventType} for {BroadcasterId}: {Status} — {Error}",
                eventType,
                broadcasterId,
                resp.StatusCode,
                err
            );
            return;
        }

        HelixDataResponse<EventSubSubscription>? result = await resp.Content.ReadFromJsonAsync<
            HelixDataResponse<EventSubSubscription>
        >(cancellationToken: ct);
        EventSubSubscription? sub = result?.Data?.FirstOrDefault();

        if (sub is not null)
        {
            _activeSubscriptions[sub.Id] = (broadcasterId, eventType);
            _logger.LogInformation(
                "EventSub: Subscribed {EventType} for {BroadcasterId} (id={SubId})",
                eventType,
                broadcasterId,
                sub.Id
            );
        }
    }

    private static Dictionary<string, string> BuildCondition(string broadcasterId, string eventType)
    {
        return eventType switch
        {
            "channel.follow" => new()
            {
                ["broadcaster_user_id"] = broadcasterId,
                ["moderator_user_id"] = broadcasterId,
            },
            "channel.raid" => new() { ["to_broadcaster_user_id"] = broadcasterId },
            "channel.chat.message"
            or "channel.chat.notification"
            or "channel.chat.message_delete" => new()
            {
                ["broadcaster_user_id"] = broadcasterId,
                ["user_id"] = broadcasterId,
            },
            "channel.shoutout.create" or "channel.shoutout.receive" => new()
            {
                ["broadcaster_user_id"] = broadcasterId,
                ["moderator_user_id"] = broadcasterId,
            },
            "channel.channel_points_custom_reward_redemption.add"
            or "channel.channel_points_custom_reward.add"
            or "channel.channel_points_custom_reward.update"
            or "channel.channel_points_custom_reward.remove" => new()
            {
                ["broadcaster_user_id"] = broadcasterId,
            },
            _ => new() { ["broadcaster_user_id"] = broadcasterId },
        };
    }

    private static string GetSubscriptionVersion(string eventType) =>
        eventType switch
        {
            "channel.follow" => "2",
            "channel.update" => "2",
            _ => "1",
        };

    // ─── Token access ─────────────────────────────────────────────────────────────

    private async Task<string?> GetBotTokenAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        IEncryptionService encryption =
            scope.ServiceProvider.GetRequiredService<IEncryptionService>();

        Service? service = await db
            .Services.Where(s =>
                s.Name == "twitch_bot"
                && s.BroadcasterId == null
                && s.Enabled
                && s.AccessToken != null
            )
            .OrderByDescending(s => s.TokenExpiry)
            .FirstOrDefaultAsync(ct);

        if (service?.AccessToken is null)
            return null;
        return encryption.TryDecrypt(service.AccessToken);
    }

    private async Task<string?> GetBroadcasterTokenAsync(string broadcasterId, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        IEncryptionService encryption =
            scope.ServiceProvider.GetRequiredService<IEncryptionService>();

        Service? service = await db.Services.FirstOrDefaultAsync(
            s =>
                s.BroadcasterId == broadcasterId
                && s.Name == "twitch"
                && s.Enabled
                && s.AccessToken != null,
            ct
        );

        if (service?.AccessToken is null)
            return null;
        return encryption.TryDecrypt(service.AccessToken);
    }

    // ─── JSON helpers ─────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class EventSubEnvelope
    {
        [JsonPropertyName("metadata")]
        public EventSubMetadata? Metadata { get; set; }

        [JsonPropertyName("payload")]
        public EventSubPayload? Payload { get; set; }
    }

    private sealed class EventSubMetadata
    {
        [JsonPropertyName("message_id")]
        public string? MessageId { get; set; }

        [JsonPropertyName("message_type")]
        public string? MessageType { get; set; }

        [JsonPropertyName("message_timestamp")]
        public string? MessageTimestamp { get; set; }
    }

    private sealed class EventSubPayload
    {
        [JsonPropertyName("session")]
        public EventSubSession? Session { get; set; }

        [JsonPropertyName("subscription")]
        public EventSubSubscription? Subscription { get; set; }

        [JsonPropertyName("event")]
        public JsonElement? Event { get; set; }
    }

    private sealed class EventSubSession
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("reconnect_url")]
        public string? ReconnectUrl { get; set; }
    }

    private sealed class EventSubSubscription
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class HelixDataResponse<T>
    {
        [JsonPropertyName("data")]
        public List<T>? Data { get; set; }
    }
}

file static class JsonElementExtensions
{
    /// <summary>
    /// Safe property accessor for JsonElement. Use as: <c>element?.GetProp("key")</c>
    /// where <c>element</c> is <c>JsonElement?</c>.
    /// </summary>
    public static string? GetProp(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement prop) ? prop.GetString() : null;
}
