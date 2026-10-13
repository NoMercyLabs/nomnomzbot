// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
/// <param name="StreamsLast14Days">How many archived streams they published in the last 14 days (Get Videos, type archive).</param>
/// <param name="LastShoutoutAt">When this channel last shouted them out, from our own records, or null when never.</param>
public sealed record ShoutoutSuggestionDto(
    string TwitchUserId,
    string Login,
    string DisplayName,
    string? Color,
    string? LastCategory,
    int StreamsLast14Days,
    DateTimeOffset? LastShoutoutAt
);
