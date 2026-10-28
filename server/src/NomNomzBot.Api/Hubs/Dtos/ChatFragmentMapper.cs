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
    private static ChatEmoteOwnerDto? MapOwner(ChatEmoteOwner? owner) =>
        owner is null ? null : new(owner.Id, owner.Login, owner.DisplayName, owner.ProfileUrl);

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
                    ZeroWidth: fragment.Emote.ZeroWidth,
                    Code: fragment.Emote.Code,
                    Type: fragment.Emote.Type,
                    Tier: fragment.Emote.Tier,
                    Owner: MapOwner(fragment.Emote.Owner),
                    Artist: MapOwner(fragment.Emote.Artist),
                    StaticUrls: fragment.Emote.StaticUrls ?? fragment.Emote.Urls
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
                : null,
            Gif: fragment.GifUrl is not null
                ? new ChatGifDto(GifId: fragment.GifId ?? string.Empty, Url: fragment.GifUrl)
                : null
        );

    public static ChatBadgeDto MapBadge(ResolvedChatBadge badge) =>
        new(badge.SetId, badge.Id, badge.Info, badge.Urls);
}
