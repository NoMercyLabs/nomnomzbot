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

// Helix "Users" category wire models (GET/PUT /users, GET/PUT/DELETE /users/blocks). These records
// deserialize straight from Twitch's snake_case JSON via the transport's naming policy — no per-property
// annotations. Twitch ids stay strings (they are users' own / other users' ids); the owning tenant is
// always passed in as a Guid method argument, never here. The email field is deliberately omitted: it
// requires the special user:read:email scope, which this client never requests.

/// <summary>
/// One user record (Get Users / Update User). <see cref="Type"/> is the staff designation
/// (<c>admin</c> / <c>global_mod</c> / <c>staff</c> / empty) and <see cref="BroadcasterType"/> is the
/// partner tier (<c>affiliate</c> / <c>partner</c> / empty); both stay plain strings because Twitch
/// returns an empty string for ordinary users. Email is intentionally not modelled (needs <c>user:read:email</c>).
/// </summary>
public sealed record TwitchUser(
    string Id,
    string Login,
    string DisplayName,
    string Type,
    string BroadcasterType,
    string Description,
    string ProfileImageUrl,
    string OfflineImageUrl,
    int ViewCount,
    DateTimeOffset CreatedAt
);

/// <summary>One user the broadcaster has blocked (Get User Block List).</summary>
public sealed record TwitchBlockedUser(string UserId, string UserLogin, string DisplayName);
