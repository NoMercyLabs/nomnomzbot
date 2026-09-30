// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The one place a channel becomes installed. The owner's sign-in, the explicit onboard endpoint and the
/// startup backfill all call this, so a brand-new channel and a moderator-mode tenant whose owner finally
/// signs in end up in the same state: onboarded, a join time, and the channel's base
/// <see cref="PlatformConnection"/> (D1). Writes stay TRACKED for the caller's own <c>SaveChanges</c>.
/// Only the channel's OWNER may trigger this; callers enforce that.
/// </summary>
internal static class ChannelOnboardingWriter
{
    public static async Task OnboardAsync(
        IApplicationDbContext db,
        Channel channel,
        string displayName,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        channel.IsOnboarded = true;
        channel.BotJoinedAt ??= now;

        await EnsurePlatformConnectionAsync(db, channel, displayName, cancellationToken);
    }

    /// <summary>
    /// Creates the channel's platform connection for its own (Provider, ExternalChannelId) when missing, or
    /// refreshes its display name. The lookup looks past soft delete because the unique index does too.
    /// </summary>
    public static async Task EnsurePlatformConnectionAsync(
        IApplicationDbContext db,
        Channel channel,
        string displayName,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(channel.ExternalChannelId))
            return;

        PlatformConnection? connection = await db
            .PlatformConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p =>
                    p.Provider == channel.Provider
                    && p.ExternalChannelId == channel.ExternalChannelId,
                cancellationToken
            );

        if (connection is not null)
        {
            if (connection.ChannelId == channel.Id && connection.DisplayName != displayName)
                connection.DisplayName = displayName;
            return;
        }

        bool hasPrimary = await db.PlatformConnections.AnyAsync(
            p => p.ChannelId == channel.Id && p.IsPrimary,
            cancellationToken
        );

        db.PlatformConnections.Add(
            new()
            {
                ChannelId = channel.Id,
                Provider = channel.Provider,
                ExternalChannelId = channel.ExternalChannelId,
                DisplayName = displayName,
                IsPrimary = !hasPrimary,
            }
        );
    }
}
