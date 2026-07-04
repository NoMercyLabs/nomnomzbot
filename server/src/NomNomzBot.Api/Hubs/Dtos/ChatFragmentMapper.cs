// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Api.Hubs.Dtos;

/// <summary>
/// Maps a decorated <see cref="ChatMessageFragment"/> / <see cref="ResolvedChatBadge"/> — the output of
/// <c>IChatMessageDecorator.DecorateAsync</c> (chat-decoration spec §0) — to its wire DTO. The single mapping
/// shared by every surface that emits decorated chat: the live <c>DashboardHub</c> broadcast
/// (<c>ChatMessageBroadcastHandler</c>) and the REST chat-history page (<c>ChatController.GetMessages</c>), so the
/// two paths can never drift into different fragment shapes for what is otherwise the same message.
/// </summary>
public static class ChatFragmentMapper
{
    public static ChatFragmentDto MapFragment(ChatMessageFragment fragment) =>
        new(
            Type: fragment.Type,
            Text: fragment.Text,
            Emote: fragment.Emote is not null
                ? new ChatEmoteDto(
                    Id: fragment.Emote.Id,
                    SetId: fragment.Emote.SetId,
                    Format: fragment.Emote.Animated ? "animated" : "static",
                    Provider: fragment.Emote.Provider.ToString(),
                    Urls: fragment.Emote.Urls,
                    Animated: fragment.Emote.Animated,
                    ZeroWidth: fragment.Emote.ZeroWidth
                )
                : null,
            Cheermote: fragment.CheermotePrefix is not null
                ? new ChatCheermoteDto(
                    Prefix: fragment.CheermotePrefix,
                    Bits: fragment.CheermoteBits ?? 0,
                    Tier: fragment.CheermoteTier ?? 1,
                    Urls: fragment.CheermoteImage?.Urls,
                    Animated: fragment.CheermoteImage?.Animated ?? false,
                    ColorHex: fragment.CheermoteImage?.ColorHex
                )
                : null,
            Mention: fragment.MentionUserId is not null
                ? new ChatMentionDto(
                    UserId: fragment.MentionUserId,
                    Username: fragment.MentionUserLogin ?? string.Empty,
                    DisplayName: fragment.MentionUserName ?? string.Empty,
                    Color: fragment.MentionColorHex
                )
                : null,
            LinkUrl: fragment.LinkUrl,
            LinkPreview: fragment.LinkPreview is not null
                ? new ChatLinkPreviewDto(
                    Host: fragment.LinkPreview.Host,
                    Title: fragment.LinkPreview.Title,
                    Description: fragment.LinkPreview.Description,
                    ImageUrl: fragment.LinkPreview.ImageUrl
                )
                : null
        );

    public static ChatBadgeDto MapBadge(ResolvedChatBadge badge) =>
        new(badge.SetId, badge.Id, badge.Info, badge.Urls);
}
