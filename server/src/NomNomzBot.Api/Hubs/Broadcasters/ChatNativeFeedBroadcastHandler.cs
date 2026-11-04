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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

public sealed class ChatNativeFeedBroadcastHandler(IDashboardNotifier notifier)
    : IEventHandler<ChatNotificationEvent>,
        IEventHandler<ChatClearedEvent>,
        IEventHandler<ChatUserMessagesClearedEvent>,
        IEventHandler<ChatUserMessageHeldEvent>,
        IEventHandler<ChatUserMessageUpdatedEvent>,
        IOnboardingExemptHandler
{
    public Task HandleAsync(ChatNotificationEvent notice, CancellationToken ct = default) =>
        SendAsync(
            new ChatMessage
            {
                Id = $"notice:{notice.MessageId}",
                BroadcasterId = notice.BroadcasterId,
                UserId = notice.ChatterUserId,
                Username = notice.ChatterLogin,
                DisplayName = notice.ChatterDisplayName,
                UserType = "viewer",
                Kind = "notice",
                NoticeType = notice.NoticeType,
                SystemText = notice.SystemMessage,
                Message = notice.MessageText,
                AnnouncementColor = notice.AnnouncementColor,
                SourceChannelId = notice.SourceChannelId,
                SourceChannelName = notice.SourceChannelName,
                SubTier = notice.SubTier,
                CumulativeMonths = notice.CumulativeMonths,
                GiftCount = notice.GiftCount,
                RaidViewerCount = notice.RaidViewerCount,
                RecipientUserId = notice.RecipientUserId,
                RecipientDisplayName = notice.RecipientDisplayName,
                CreatedAt = notice.OccurredAt.UtcDateTime,
            },
            ct
        );

    public Task HandleAsync(ChatClearedEvent cleared, CancellationToken ct = default) =>
        SendAsync(
            new ChatMessage
            {
                Id = $"moderation:{cleared.EventId}",
                BroadcasterId = cleared.BroadcasterId,
                UserId = cleared.ClearedByUserId,
                Username = string.Empty,
                DisplayName = string.Empty,
                UserType = "viewer",
                Kind = "moderation",
                NoticeType = "chat_clear",
                Message = string.Empty,
                CreatedAt = cleared.OccurredAt.UtcDateTime,
            },
            ct
        );

    public Task HandleAsync(ChatUserMessagesClearedEvent cleared, CancellationToken ct = default) =>
        SendAsync(
            new ChatMessage
            {
                Id = $"moderation:{cleared.EventId}",
                BroadcasterId = cleared.BroadcasterId,
                UserId = cleared.TargetUserId,
                Username = cleared.TargetUserLogin,
                DisplayName = cleared.TargetUserDisplayName,
                UserType = "viewer",
                Kind = "moderation",
                NoticeType = "chat_clear_user",
                Message = string.Empty,
                CreatedAt = cleared.OccurredAt.UtcDateTime,
            },
            ct
        );

    public Task HandleAsync(ChatUserMessageHeldEvent held, CancellationToken ct = default) =>
        SendHeldAsync(
            new ChatMessage
            {
                Id = $"held:{held.MessageId}",
                BroadcasterId = held.BroadcasterId,
                UserId = held.UserId,
                Username = held.UserLogin,
                DisplayName = held.UserDisplayName,
                UserType = "viewer",
                Kind = "held",
                NoticeType = "hold",
                HeldStatus = "held",
                Message = held.Text,
                CreatedAt = held.OccurredAt.UtcDateTime,
            },
            ct
        );

    public Task HandleAsync(ChatUserMessageUpdatedEvent updated, CancellationToken ct = default) =>
        SendHeldAsync(
            new ChatMessage
            {
                Id = $"held-update:{updated.MessageId}:{updated.EventId}",
                BroadcasterId = updated.BroadcasterId,
                UserId = updated.UserId,
                Username = updated.UserLogin,
                DisplayName = updated.UserDisplayName,
                UserType = "viewer",
                Kind = "held",
                NoticeType = "update",
                HeldStatus = updated.Status,
                Message = updated.Text,
                CreatedAt = updated.OccurredAt.UtcDateTime,
            },
            ct
        );

    private Task SendAsync(ChatMessage row, CancellationToken ct)
    {
        string channelId = row.BroadcasterId.ToString();
        DashboardChatMessageDto dto = ChatHistoryMapper.MapNativeRow(row, channelId);
        return notifier.SendChatMessageAsync(channelId, dto, ct);
    }

    private Task SendHeldAsync(ChatMessage row, CancellationToken ct)
    {
        string channelId = row.BroadcasterId.ToString();
        DashboardChatMessageDto dto = ChatHistoryMapper.MapNativeRow(row, channelId);
        return notifier.SendHeldChatMessageAsync(channelId, dto, ct);
    }
}
