// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Maps persisted chat rows (in the order given) to the SAME <see cref="DashboardChatMessageDto"/> the live hub
/// emits (chat-client.md §3.6), so scrollback and the live feed render identically. Shared by the operator chat
/// page and viewer chat (§9a).
/// </summary>
public static class ChatHistoryMapper
{
    public static async Task<List<DashboardChatMessageDto>> MapAsync(
        IApplicationDbContext db,
        IChatMessageDecorator decorator,
        IHubUserEnricher enricher,
        Guid broadcasterId,
        string channelId,
        IReadOnlyList<ChatMessage> rows,
        CancellationToken ct
    )
    {
        // One lookup for the channel's Twitch id so the third-party emote adapters match this channel's OWN emote
        // set (chat-decoration spec §3.2). Empty when the channel row is missing: decoration then falls back to
        // global-only sets, like the live hub path on an unknown channel.
        string twitchBroadcasterId =
            await db
                .Channels.Where(c => c.Id == broadcasterId)
                .Select(c => c.TwitchChannelId)
                .FirstOrDefaultAsync(ct)
            ?? string.Empty;

        // Sequential on purpose: each message stays on the SAME request-scoped DbContext one at a time, avoiding the
        // "second operation on this context" hazard a concurrent fan-out would risk. Both steps are cache-backed.
        List<DashboardChatMessageDto> messages = new(rows.Count);
        foreach (ChatMessage row in rows)
        {
            DecoratedChatMessage decorated = await decorator.DecorateAsync(
                ToDecorationEvent(row, twitchBroadcasterId),
                ct
            );
            HubUserEnrichment? enrichment = await enricher.EnrichAsync(
                broadcasterId,
                row.UserId,
                ct
            );
            (bool isBroadcaster, bool isModerator, bool isVip, bool isSubscriber) = ParseUserType(
                row.UserType
            );

            messages.Add(
                new(
                    Id: row.Id,
                    ChannelId: channelId,
                    UserId: row.UserId,
                    DisplayName: row.DisplayName,
                    Username: row.Username,
                    Message: row.Message,
                    Fragments: [.. decorated.Fragments.Select(ChatFragmentMapper.MapFragment)],
                    UserType: row.UserType,
                    IsSubscriber: isSubscriber,
                    IsVip: isVip,
                    IsModerator: isModerator,
                    IsBroadcaster: isBroadcaster,
                    IsCheer: row.IsCheer,
                    IsCommand: row.IsCommand,
                    Badges: [.. decorated.Badges.Select(ChatFragmentMapper.MapBadge)],
                    BitsAmount: row.BitsAmount ?? 0,
                    Color: row.ColorHex,
                    MessageType: row.MessageType,
                    ReplyToMessageId: row.ReplyToMessageId,
                    ReplyParentMessageBody: null,
                    ReplyParentUserName: null,
                    Timestamp: row.CreatedAt.ToString("O"),
                    AvatarUrl: enrichment?.AvatarUrl,
                    Pronouns: enrichment?.Pronouns,
                    Provider: row.Provider
                )
            );
        }

        return messages;
    }

    // Rebuilds the minimal ChatMessageReceivedEvent the decorator needs from a persisted (raw, un-enriched) row, so
    // the exact decorator pipeline of the live hub broadcast also decorates chat history.
    private static ChatMessageReceivedEvent ToDecorationEvent(
        ChatMessage row,
        string twitchBroadcasterId
    )
    {
        (bool isBroadcaster, bool isModerator, bool isVip, bool isSubscriber) = ParseUserType(
            row.UserType
        );

        return new()
        {
            BroadcasterId = row.BroadcasterId,
            MessageId = row.Id,
            TwitchBroadcasterId = twitchBroadcasterId,
            UserId = row.UserId,
            UserDisplayName = row.DisplayName,
            UserLogin = row.Username,
            Message = row.Message,
            Fragments = row.Fragments,
            Badges = row.Badges,
            ColorHex = row.ColorHex,
            MessageType = row.MessageType,
            IsSubscriber = isSubscriber,
            IsVip = isVip,
            IsModerator = isModerator,
            IsBroadcaster = isBroadcaster,
            Bits = row.BitsAmount ?? 0,
            ReplyParentMessageId = row.ReplyToMessageId,
        };
    }

    // The reverse of ChatMessagePersistenceHandler.ResolveUserType's priority (broadcaster > moderator > vip >
    // subscriber), recovering the role flags the decorator's link-preview standing gate needs (chat-decoration §9·9).
    private static (
        bool IsBroadcaster,
        bool IsModerator,
        bool IsVip,
        bool IsSubscriber
    ) ParseUserType(string userType) =>
        userType switch
        {
            "broadcaster" => (true, false, false, false),
            "moderator" => (false, true, false, false),
            "vip" => (false, false, true, false),
            "subscriber" => (false, false, false, true),
            _ => (false, false, false, false),
        };
}
