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

namespace NomNomzBot.Application.Chat.Dtos;

/// <summary>
/// How the channel owner looks in chat: the name color Twitch holds, the 15 named colors every user may
/// pick, whether the connection holds the scope to change the color, and the badges the owner wore on
/// their latest chat line, resolved to images from the global and channel badge sets. Twitch has no API
/// to read or change which badge a user displays.
/// </summary>
public sealed record ChatIdentityDto(
    string DisplayName,
    string? Color,
    bool CanManageColor,
    string ColorScope,
    IReadOnlyList<ChatNamedColorDto> NamedColors,
    IReadOnlyList<ChatIdentityBadgeDto> Badges
);

/// <summary>One Twitch named chat color: the value Twitch accepts and the hex it renders as.</summary>
public sealed record ChatNamedColorDto(string Name, string Hex);

/// <summary>One badge the owner wears in chat, with its image at 1x, 2x and 4x.</summary>
public sealed record ChatIdentityBadgeDto(
    string SetId,
    string Id,
    string? Info,
    IReadOnlyDictionary<string, string> Urls
);

/// <summary>The new chat color: a Twitch color name (blue, hot_pink, ...) or a hex value like #9146FF.</summary>
public sealed record UpdateChatColorDto(string Color);
