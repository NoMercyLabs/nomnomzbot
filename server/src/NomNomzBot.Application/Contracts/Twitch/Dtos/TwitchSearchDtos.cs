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

// Helix "Search" category wire models (GET /search/categories, /search/channels). These records
// deserialize straight from Twitch's snake_case JSON via the transport's naming policy — no per-property
// annotations. Both endpoints are App-token, free-text reads: there is no owning tenant here, so every id
// stays a string (other channels' / games' ids) and timestamps map to DateTimeOffset.

/// <summary>One game or category that matched a Search Categories query.</summary>
public sealed record TwitchSearchCategory(string Id, string Name, string BoxArtUrl);

/// <summary>
/// One channel that matched a Search Channels query (channels that have streamed within the past 6 months).
/// <c>IsLive</c> reflects the channel's live state at query time; <c>StartedAt</c> is the current stream's
/// start (default value when the channel is offline).
/// </summary>
public sealed record TwitchSearchChannel(
    string Id,
    string BroadcasterLogin,
    string DisplayName,
    string BroadcasterLanguage,
    string GameId,
    string GameName,
    bool IsLive,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> TagIds,
    string ThumbnailUrl,
    string Title,
    DateTimeOffset StartedAt
);
