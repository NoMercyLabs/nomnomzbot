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
