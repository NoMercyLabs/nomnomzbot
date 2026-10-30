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
using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Admin.Diagnostics.Connections;

/// <summary>The Discord servers linked to the channel. A server is connected while the bot is installed in it.</summary>
public sealed class DiscordGuildConnectionSource(IApplicationDbContext db)
    : IChannelConnectionSource
{
    public async Task<IReadOnlyList<ChannelDiagnosticConnectionDto>> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Row> rows = await db
            .DiscordGuildConnections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.BroadcasterId == broadcasterId && c.DeletedAt == null)
            .Select(c => new Row(
                c.Id,
                c.GuildId,
                c.GuildName,
                c.BotInstalled,
                c.ServerConsentStatus,
                c.ApprovedAt
            ))
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => new ChannelDiagnosticConnectionDto
            {
                Kind = ChannelConnectionKinds.DiscordGuild,
                Provider = AuthEnums.IntegrationProvider.Discord,
                Id = r.Id.ToString(),
                AccountName = r.GuildName,
                AccountId = r.GuildId,
                Connected = r.BotInstalled,
                Status = r.ConsentStatus,
                LastActivityAt = r.ApprovedAt,
            }),
        ];
    }

    private sealed record Row(
        Guid Id,
        string GuildId,
        string? GuildName,
        bool BotInstalled,
        string ConsentStatus,
        DateTime? ApprovedAt
    );
}
