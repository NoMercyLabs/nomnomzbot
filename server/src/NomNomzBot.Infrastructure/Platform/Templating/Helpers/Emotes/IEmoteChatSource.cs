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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>One stored chat line, reduced to what an emote count needs.</summary>
/// <param name="UserId">The chatter's id on the platform (the Twitch id on a Twitch channel).</param>
/// <param name="Text">The message text as typed.</param>
/// <param name="StreamId">The stream the line belongs to; null for a line sent while offline.</param>
internal sealed record EmoteChatLine(string UserId, string Text, string? StreamId);

/// <summary>The stored chat and the channel owner behind the emote usage and favorite rows.</summary>
internal interface IEmoteChatSource
{
    /// <summary>The newest non-command chat lines of the channel, newest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<EmoteChatLine>> RecentLinesAsync(
        IServiceProvider services,
        Guid broadcasterId,
        int take,
        CancellationToken cancellationToken
    );

    /// <summary>The user who owns the channel (the operator whose emote favorites the dashboard keeps); null when unknown.</summary>
    Task<Guid?> OwnerUserIdAsync(
        IServiceProvider services,
        Guid broadcasterId,
        CancellationToken cancellationToken
    );
}
