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

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Tells the channel when Twitch refuses the bot's chat send there and when it works again, so a bot that is
/// silent in a channel is never silent about why. Reports are throttled per channel: a busy chat of refused
/// replies raises one notice, not one per message.
/// </summary>
public interface IBotChatSendRefusalReporter
{
    /// <summary>Records that Twitch refused <paramref name="botUsername"/>'s send in the channel.</summary>
    Task ReportRefusedAsync(
        Guid channelId,
        string botUsername,
        CancellationToken cancellationToken = default
    );

    /// <summary>Records that a send went through. Clears the notice when a refusal was reported before.</summary>
    Task ReportDeliveredAsync(Guid channelId, CancellationToken cancellationToken = default);
}
