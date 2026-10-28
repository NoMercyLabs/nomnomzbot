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

using System.Text.Json;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// One moderator's chat-read EventSub session: the moderated channels it holds, eight subscriptions each,
/// created on the moderator's own token. Frames of held channels go on to the bot pipeline; a revocation tells
/// the coordinator why a channel was lost.
/// </summary>
internal sealed class ModeratorChatReader : IEventSubNotificationSink
{
    /// <summary>Twitch allows 300 subscription cost per session; eight subscriptions per channel fit 37.</summary>
    public const int MaxChannels = 37;

    private readonly string _moderatorTwitchId;
    private readonly IViewerEventSubSessions _sessions;
    private readonly IEventSubNotificationSink _pipeline;
    private readonly Func<string, ModeratorChatReaderState, Task> _onLost;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly HashSet<string> _channels = [];

    public ModeratorChatReader(
        Guid moderatorUserId,
        string moderatorTwitchId,
        IViewerEventSubSessions sessions,
        IEventSubNotificationSink pipeline,
        Func<string, ModeratorChatReaderState, Task> onLost,
        ILogger logger
    )
    {
        ModeratorUserId = moderatorUserId;
        OwnerKey = EventSubOwnerKeys.Moderator(moderatorUserId);
        _moderatorTwitchId = moderatorTwitchId;
        _sessions = sessions;
        _pipeline = pipeline;
        _onLost = onLost;
        _logger = logger;
    }

    public Guid ModeratorUserId { get; }
    public string OwnerKey { get; }
    public string? SessionId { get; set; }

    public int ChannelCount
    {
        get
        {
            lock (_lock)
                return _channels.Count;
        }
    }

    public bool Holds(string twitchBroadcasterId)
    {
        lock (_lock)
            return _channels.Contains(twitchBroadcasterId);
    }

    public IReadOnlyList<string> HeldChannels()
    {
        lock (_lock)
            return [.. _channels];
    }

    public async Task<Result> AddChannelAsync(string twitchBroadcasterId, CancellationToken ct)
    {
        lock (_lock)
            _channels.Add(twitchBroadcasterId);

        Result subscribed = await SubscribeAsync(twitchBroadcasterId, ct);
        if (subscribed.IsFailure)
            Remove(twitchBroadcasterId);
        return subscribed;
    }

    public void Remove(string twitchBroadcasterId)
    {
        lock (_lock)
            _channels.Remove(twitchBroadcasterId);
    }

    private async Task<Result> SubscribeAsync(string twitchBroadcasterId, CancellationToken ct)
    {
        string? sessionId = SessionId;
        if (sessionId is null)
            return Result.Failure("The moderator's Twitch session is not open.", "no_session");

        Dictionary<string, string> condition = new()
        {
            ["broadcaster_user_id"] = twitchBroadcasterId,
            ["user_id"] = _moderatorTwitchId,
        };
        foreach (string topic in EventSubConditionBuilder.ChatReadEvents)
        {
            Result<string> created = await _sessions.SubscribeAsViewerAsync(
                ModeratorUserId,
                sessionId,
                topic,
                "1",
                condition,
                ct
            );
            if (created.IsFailure)
                return created;
        }

        return Result.Success();
    }

    // ── IEventSubNotificationSink ───────────────────────────────────────────────

    public async Task OnSessionWelcomeAsync(
        string sessionId,
        string ownerKey,
        string? handoffFromSessionId,
        CancellationToken ct
    )
    {
        string? previous = SessionId;
        SessionId = sessionId;
        // A fresh session after a drop carries none of the old subscriptions; a reconnect handoff keeps them.
        if (previous is null || previous == sessionId || handoffFromSessionId is not null)
            return;

        foreach (string channel in HeldChannels())
        {
            Result resubscribed = await SubscribeAsync(channel, ct);
            if (resubscribed.IsFailure)
                _logger.LogWarning(
                    "Moderator chat: re-subscribing {Channel} failed: {Error}",
                    channel,
                    resubscribed.ErrorMessage
                );
        }
    }

    public Task<bool> ShouldReconnectAsync(string ownerKey, CancellationToken ct) =>
        Task.FromResult(ChannelCount > 0);

    public Task OnNotificationAsync(
        string messageId,
        DateTimeOffset messageTimestamp,
        string subscriptionType,
        string subscriptionVersion,
        string twitchBroadcasterUserId,
        JsonElement @event,
        CancellationToken ct
    ) =>
        Holds(twitchBroadcasterUserId)
            ? _pipeline.OnNotificationAsync(
                messageId,
                messageTimestamp,
                subscriptionType,
                subscriptionVersion,
                twitchBroadcasterUserId,
                @event,
                ct
            )
            : Task.CompletedTask;

    public async Task OnRevocationAsync(
        string twitchSubscriptionId,
        string subscriptionType,
        string status,
        string twitchBroadcasterUserId,
        CancellationToken ct
    )
    {
        _logger.LogInformation(
            "Moderator chat: {Type} for {Channel} revoked ({Status})",
            subscriptionType,
            twitchBroadcasterUserId,
            status
        );

        switch (status)
        {
            case "moderator_removed":
                await LoseAsync(twitchBroadcasterUserId, ModeratorChatReaderState.Unmodded);
                break;
            case "authorization_revoked" or "user_removed":
                foreach (string channel in HeldChannels())
                    await LoseAsync(channel, ModeratorChatReaderState.NoToken);
                break;
        }
    }

    private async Task LoseAsync(string twitchBroadcasterId, ModeratorChatReaderState state)
    {
        bool held;
        lock (_lock)
            held = _channels.Remove(twitchBroadcasterId);
        if (held)
            await _onLost(twitchBroadcasterId, state);
    }

    public Task OnSessionDisconnectedAsync(
        string ownerKey,
        string? sessionId,
        string reason,
        TimeSpan nextRetryIn,
        CancellationToken ct
    ) => Task.CompletedTask;
}
