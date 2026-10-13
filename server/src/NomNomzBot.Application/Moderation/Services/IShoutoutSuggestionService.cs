// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Suggests who to shout out during the current stream: chatters of this stream who streamed often in the last
/// 14 days, most streams first. Every field comes from Twitch or from this channel's own records.
/// </summary>
public interface IShoutoutSuggestionService
{
    /// <summary>
    /// Up to 10 suggestions. The list is empty when the channel is offline. The broadcaster, the bot, chatters
    /// without an archive in the last 14 days, chatters already shouted out this stream and chatters dismissed
    /// this stream are left out.
    /// </summary>
    Task<Result<IReadOnlyList<ShoutoutSuggestionDto>>> GetAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Hides one chatter for the rest of the current stream ("Not now").</summary>
    Result Dismiss(Guid channelId, string twitchUserId);
}
