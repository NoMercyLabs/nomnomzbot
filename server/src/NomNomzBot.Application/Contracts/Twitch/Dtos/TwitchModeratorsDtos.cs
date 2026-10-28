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

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Moderators & VIPs" category wire models (GET /moderation/moderators, GET /channels/vips,
// GET /moderation/channels). These records deserialize straight from Twitch's snake_case JSON via the
// transport's naming policy — no per-property annotations. Twitch ids stay strings (they are other
// users' / channels' ids); the owning tenant is always passed in as a Guid method argument, never here.

/// <summary>One user allowed to moderate the broadcaster's chat room (Get Moderators).</summary>
public sealed record TwitchModerator(string UserId, string UserLogin, string UserName);

/// <summary>One VIP of the broadcaster's channel (Get VIPs).</summary>
public sealed record TwitchVip(string UserId, string UserName, string UserLogin);

/// <summary>One channel the user has moderator privileges in (Get Moderated Channels).</summary>
public sealed record TwitchModeratedChannel(
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName
);
