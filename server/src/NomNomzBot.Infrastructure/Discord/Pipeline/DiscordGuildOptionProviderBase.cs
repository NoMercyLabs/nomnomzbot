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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Discord.Entities;

namespace NomNomzBot.Infrastructure.Discord.Pipeline;

/// <summary>
/// Shared "resolve the tenant's active Discord guild link, or report why there is none" step for the
/// <c>discord_channel</c>/<c>discord_role</c> option providers (S-RICH-PICKERS). A link is active only when the
/// server admin approved AND the streamer enabled it — same rule as <c>DiscordGuildService</c>'s own
/// <c>IsActive</c> predicate.
/// </summary>
internal abstract class DiscordGuildOptionProviderBase
{
    private const string Approved = "approved";
    private readonly IApplicationDbContext _db;

    protected DiscordGuildOptionProviderBase(IApplicationDbContext db)
    {
        _db = db;
    }

    protected async Task<Result<DiscordGuildConnection>> ResolveActiveConnectionAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        DiscordGuildConnection? connection = await _db
            .DiscordGuildConnections.Where(c =>
                c.BroadcasterId == broadcasterId
                && c.ServerConsentStatus == Approved
                && c.StreamerEnabled
            )
            .FirstOrDefaultAsync(ct);

        return connection is null
            ? Result.Failure<DiscordGuildConnection>(
                "No active Discord server link for this channel — link and enable a server in the Discord integration settings."
            )
            : Result.Success(connection);
    }
}
