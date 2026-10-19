// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The one rule for where an admin network block may act: only in a channel whose owner opted in
/// (<see cref="Channel.AcceptsNetworkBlocks"/>, off by default). Apply, the chat-time leg and the onboarding leg
/// all read it, so no path can reach a channel that did not ask.
/// </summary>
public static class NetworkBlockReach
{
    public static IQueryable<Channel> OptedIn(this IQueryable<Channel> channels) =>
        channels.Where(c => c.AcceptsNetworkBlocks);
}
