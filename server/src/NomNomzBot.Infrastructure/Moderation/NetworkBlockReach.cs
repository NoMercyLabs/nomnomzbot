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
