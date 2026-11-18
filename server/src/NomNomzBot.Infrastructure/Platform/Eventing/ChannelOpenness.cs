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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The one rule for "may the bot act in this channel?", shared by the event bus gate and the chat router.
/// A channel is open only when its broadcaster onboarded. In a channel that did not onboard, the bot only
/// watches for spam through the onboarding-exempt handlers; it never speaks, greets or answers commands there,
/// even when an onboarded user moderates it (Stoney, 2026-11-18: "you were to monitor channels for spam that are
/// not onboarded").
/// </summary>
public static class ChannelOpenness
{
    public static Task<bool> IsClosedAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        CancellationToken cancellationToken
    ) => db.Channels.AnyAsync(c => c.Id == broadcasterId && !c.IsOnboarded, cancellationToken);

    /// <summary>Every open channel, by the same rule <see cref="IsClosedAsync"/> uses.</summary>
    public static IQueryable<Channel> OpenChannels(IApplicationDbContext db) =>
        db.Channels.Where(c => c.IsOnboarded);
}
