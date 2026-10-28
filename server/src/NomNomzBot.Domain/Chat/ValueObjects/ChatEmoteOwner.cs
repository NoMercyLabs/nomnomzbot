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

namespace NomNomzBot.Domain.Chat.ValueObjects;

/// <summary>
/// Who owns an emote. For a Twitch emote this is the owning channel (platform user id, login, display name). For a
/// 7TV, BTTV or FFZ emote it is the uploader as that provider returns it. A field the provider or cache does not
/// have is null.
/// </summary>
/// <param name="Id">The owner id at the emote's service.</param>
/// <param name="Login">The owner login name (lowercase), when known.</param>
/// <param name="DisplayName">The owner display name, when known.</param>
/// <param name="ProfileUrl">A public profile link for the owner, when the provider has one.</param>
/// <param name="AvatarUrl">The owner profile image link, when the platform has one (Twitch owners).</param>
public sealed record ChatEmoteOwner(
    string? Id,
    string? Login,
    string? DisplayName,
    string? ProfileUrl = null,
    string? AvatarUrl = null
);
