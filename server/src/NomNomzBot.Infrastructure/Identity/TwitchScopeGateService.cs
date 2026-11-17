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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Reads the scopes stored on the channel's Twitch connection and, when one is missing, hands the gap to
/// <see cref="IScopeNotificationService"/>, whose record is one row per channel and scope, so a repeat run never
/// writes a second inbox item. A channel with no Twitch connection has nothing to attribute a gap to, so it is
/// skipped without a record.
/// </summary>
public sealed class TwitchScopeGateService(
    IApplicationDbContext db,
    IScopeNotificationService scopeNotifications
) : ITwitchScopeGateService
{
    public async Task<bool> HasScopeOrRecordAsync(
        Guid broadcasterId,
        string scope,
        CancellationToken ct = default
    )
    {
        IntegrationConnection? connection = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == broadcasterId
                    && c.Provider == AuthEnums.IntegrationProvider.Twitch
                    && c.DeletedAt == null,
                ct
            );
        if (connection is null)
            return false;

        if (connection.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
            return true;

        await scopeNotifications.RecordMissingScopeAsync(broadcasterId, scope, null, ct);
        return false;
    }
}
