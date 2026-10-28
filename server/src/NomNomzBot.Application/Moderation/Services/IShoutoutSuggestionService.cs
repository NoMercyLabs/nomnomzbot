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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Suggests who to shout out during the current stream: chatters of this stream who streamed often in the last
/// 30 days, most streams first. Every field comes from Twitch or from this channel's own records.
/// </summary>
public interface IShoutoutSuggestionService
{
    /// <summary>
    /// Up to 10 suggestions. The list is empty when the channel is offline. The broadcaster, the bot, chatters
    /// without an archive in the last 30 days, chatters already shouted out this stream and chatters dismissed
    /// this stream are left out.
    /// </summary>
    Task<Result<IReadOnlyList<ShoutoutSuggestionDto>>> GetAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Hides one chatter for the rest of the current stream ("Not now").</summary>
    Result Dismiss(Guid channelId, string twitchUserId);
}
