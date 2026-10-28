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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Dtos;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// One viewer's off-bot chat session: the channels each of the viewer's connections reads, and the sink that
/// turns the session's frames into <see cref="ExternalChatEvent"/> pushes for those connections only.
/// </summary>
internal sealed class ExternalChatReader : IEventSubNotificationSink
{
    private static readonly string[] Topics =
    [
        "channel.chat.message",
        "channel.chat.message_delete",
        "channel.chat.clear",
    ];

    private readonly string _viewerTwitchId;
    private readonly IViewerEventSubSessions _sessions;
    private readonly IExternalChatPush _push;
    private readonly IChatBadgeResolver _badges;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();

    // channel -> the connections that read it. A channel stays in _subscribed after its last connection
    // leaves: its subscriptions live until the session closes, so a re-join needs no second create.
    private readonly Dictionary<string, HashSet<string>> _channels = [];
    private readonly HashSet<string> _subscribed = [];

    public ExternalChatReader(
        Guid viewerUserId,
        string viewerTwitchId,
        IViewerEventSubSessions sessions,
        IExternalChatPush push,
        IChatBadgeResolver badges,
        ILogger logger
    )
    {
        ViewerUserId = viewerUserId;
        OwnerKey = EventSubOwnerKeys.Viewer(viewerUserId);
        _viewerTwitchId = viewerTwitchId;
        _sessions = sessions;
        _push = push;
        _badges = badges;
        _logger = logger;
    }

    public Guid ViewerUserId { get; }
    public string OwnerKey { get; }
    public string? SessionId { get; set; }

    public bool IsIdle
    {
        get
        {
            lock (_lock)
                return _channels.Count == 0;
        }
    }

    public bool HasConnection(string connectionId)
    {
        lock (_lock)
            return _channels.Values.Any(connections => connections.Contains(connectionId));
    }

    public async Task<Result> JoinAsync(
        string connectionId,
        string twitchBroadcasterId,
        CancellationToken ct
    )
    {
        lock (_lock)
        {
            if (!_channels.TryGetValue(twitchBroadcasterId, out HashSet<string>? connections))
                _channels[twitchBroadcasterId] = connections = [];
            connections.Add(connectionId);
        }

        return await EnsureSubscribedAsync(twitchBroadcasterId, ct);
    }

    public void Leave(string connectionId, string twitchBroadcasterId)
    {
        lock (_lock)
        {
            if (!_channels.TryGetValue(twitchBroadcasterId, out HashSet<string>? connections))
                return;
            connections.Remove(connectionId);
            if (connections.Count == 0)
                _channels.Remove(twitchBroadcasterId);
        }
    }

    public void DropConnection(string connectionId)
    {
        lock (_lock)
        {
            foreach (string channel in _channels.Keys.ToList())
                Leave(connectionId, channel);
        }
    }

    private async Task<Result> EnsureSubscribedAsync(
        string twitchBroadcasterId,
        CancellationToken ct
    )
    {
        string? sessionId = SessionId;
        lock (_lock)
        {
            if (sessionId is null || _subscribed.Contains(twitchBroadcasterId))
                return Result.Success();
        }

        Dictionary<string, string> condition = new()
        {
            ["broadcaster_user_id"] = twitchBroadcasterId,
            ["user_id"] = _viewerTwitchId,
        };
        foreach (string topic in Topics)
        {
            Result<string> created = await _sessions.SubscribeAsViewerAsync(
                ViewerUserId,
                sessionId,
                topic,
                "1",
                condition,
                ct
            );
            if (created.IsFailure)
                return created;
        }

        lock (_lock)
            _subscribed.Add(twitchBroadcasterId);
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

        List<string> channels;
        lock (_lock)
        {
            _subscribed.Clear();
            channels = [.. _channels.Keys];
        }

        foreach (string channel in channels)
        {
            Result resubscribed = await EnsureSubscribedAsync(channel, ct);
            if (resubscribed.IsFailure)
                _logger.LogWarning(
                    "Off-bot chat: re-subscribing {Channel} failed: {Error}",
                    channel,
                    resubscribed.ErrorMessage
                );
        }
    }

    public Task<bool> ShouldReconnectAsync(string ownerKey, CancellationToken ct) =>
        Task.FromResult(!IsIdle);

    public async Task OnNotificationAsync(
        string messageId,
        DateTimeOffset messageTimestamp,
        string subscriptionType,
        string subscriptionVersion,
        string twitchBroadcasterUserId,
        JsonElement @event,
        CancellationToken ct
    )
    {
        ExternalChatEvent? chatEvent = ExternalChatEventMapper.Map(
            subscriptionType,
            twitchBroadcasterUserId,
            messageTimestamp,
            @event
        );
        if (chatEvent is null)
            return;

        List<string> connections;
        lock (_lock)
        {
            connections = _channels.TryGetValue(twitchBroadcasterUserId, out HashSet<string>? set)
                ? [.. set]
                : [];
        }

        if (connections.Count == 0)
            return;

        ExternalChatEvent decorated = await DecorateAsync(chatEvent, ct);
        foreach (string connectionId in connections)
            await _push.PushAsync(connectionId, decorated, ct);
    }

    // The bot pipeline never sees these lines, so the reader gives them the channel's own badge images and the
    // Twitch emote images (their CDN urls follow from the emote id alone).
    private async Task<ExternalChatEvent> DecorateAsync(
        ExternalChatEvent line,
        CancellationToken ct
    )
    {
        foreach (ChatMessageFragment fragment in line.Fragments)
            if (fragment is { Type: "emote", EmoteId: not null, Emote: null })
                fragment.Emote = TwitchFragmentEmote(fragment, fragment.EmoteId);

        if (line.Badges.Count == 0)
            return line;

        List<ChatBadge> raw = [.. line.Badges.Select(b => new ChatBadge(b.SetId, b.Id, b.Info))];
        IReadOnlyList<ResolvedChatBadge> badges = await _badges.ResolveByTwitchIdAsync(
            line.BroadcasterTwitchId,
            raw,
            ct
        );
        return line with { Badges = badges };
    }

    private static ChatEmote TwitchFragmentEmote(ChatMessageFragment fragment, string emoteId)
    {
        bool animated = fragment.EmoteFormats.Contains("animated");

        return new(
            EmoteProvider.Twitch,
            emoteId,
            fragment.Text,
            TwitchEmoteMapper.BuildUrls(emoteId, animated),
            animated,
            ZeroWidth: false,
            SetId: fragment.EmoteSetId,
            OwnerId: fragment.EmoteOwnerId,
            Formats: fragment.EmoteFormats
        );
    }

    public Task OnRevocationAsync(
        string twitchSubscriptionId,
        string subscriptionType,
        string status,
        string twitchBroadcasterUserId,
        CancellationToken ct
    )
    {
        lock (_lock)
            _subscribed.Remove(twitchBroadcasterUserId);
        _logger.LogInformation(
            "Off-bot chat: {Type} for {Channel} revoked ({Status})",
            subscriptionType,
            twitchBroadcasterUserId,
            status
        );
        return Task.CompletedTask;
    }

    public Task OnSessionDisconnectedAsync(
        string ownerKey,
        string? sessionId,
        string reason,
        TimeSpan nextRetryIn,
        CancellationToken ct
    ) => Task.CompletedTask;
}
