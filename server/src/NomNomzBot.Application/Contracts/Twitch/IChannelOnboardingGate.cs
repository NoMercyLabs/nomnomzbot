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

/// <summary>
/// The one answer to "may the bot act in this channel?": a channel whose broadcaster has not onboarded must
/// never get a bot reply, command or automation; its chat may only be stored.
/// </summary>
public interface IChannelOnboardingGate
{
    /// <summary>
    /// True when a channel row exists and its broadcaster has NOT onboarded. A channel with no row (and the
    /// platform sentinel <see cref="Guid.Empty"/>) is not closed: nothing can act in a channel that is unknown.
    /// </summary>
    Task<bool> IsClosedAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
}
