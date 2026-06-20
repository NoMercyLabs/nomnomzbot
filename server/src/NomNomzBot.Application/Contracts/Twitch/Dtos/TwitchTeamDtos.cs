// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Teams" category wire models (GET /teams/channel, /teams). These records deserialize straight
// from Twitch's snake_case JSON via the transport's naming policy — no per-property annotations. Twitch
// ids stay strings (team ids and other channels'/users' ids); the owning tenant is always passed in as a
// Guid method argument, never here. Timestamps are DateTimeOffset.

/// <summary>One Twitch team the channel belongs to (Get Channel Teams) — team metadata plus the channel's identity.</summary>
public sealed record TwitchChannelTeam(
    string Id,
    string TeamName,
    string TeamDisplayName,
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    string BackgroundImageUrl,
    string Banner,
    string Info,
    string ThumbnailUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

/// <summary>One member of a Twitch team (Get Teams).</summary>
public sealed record TwitchTeamMember(string UserId, string UserLogin, string UserName);

/// <summary>A specific Twitch team and its full member list (Get Teams) — team metadata plus every member.</summary>
public sealed record TwitchTeam(
    string Id,
    string TeamName,
    string TeamDisplayName,
    IReadOnlyList<TwitchTeamMember> Users,
    string BackgroundImageUrl,
    string Banner,
    string Info,
    string ThumbnailUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
