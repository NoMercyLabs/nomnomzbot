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

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// One chatter of the current stream the streamer may want to shout out.
/// </summary>
/// <param name="TwitchUserId">The chatter's Twitch user id.</param>
/// <param name="Login">The chatter's Twitch login name.</param>
/// <param name="DisplayName">The name as Twitch shows it.</param>
/// <param name="Color">The chat color from their latest chat line in this channel, or null when none is known.</param>
/// <param name="LastCategory">The category of their channel on Twitch now (Get Channel Information), or null when none is set.</param>
/// <param name="StreamsLast30Days">How many archived streams they published in the last 30 days (Get Videos, type archive).</param>
/// <param name="LastShoutoutAt">When this channel last shouted them out, from our own records, or null when never.</param>
/// <param name="ProfileImageUrl">The chatter's Twitch profile picture (Get Users), or null when Twitch gave none.</param>
public sealed record ShoutoutSuggestionDto(
    string TwitchUserId,
    string Login,
    string DisplayName,
    string? Color,
    string? LastCategory,
    int StreamsLast30Days,
    DateTimeOffset? LastShoutoutAt,
    string? ProfileImageUrl = null
);
