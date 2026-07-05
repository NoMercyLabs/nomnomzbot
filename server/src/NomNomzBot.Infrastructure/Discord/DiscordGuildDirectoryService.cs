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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// Live guild directory for the dashboard's role/channel pickers (ROADMAP guild read endpoints). Resolves the
/// tenant's <c>DiscordGuildConnection</c> row to its <c>GuildId</c> (NOT_FOUND if absent or other-tenant), then
/// proxies the read through <see cref="IDiscordBotGateway"/> — pure reads, nothing persisted.
/// </summary>
public sealed class DiscordGuildDirectoryService : IDiscordGuildDirectoryService
{
    private readonly IApplicationDbContext _db;
    private readonly IDiscordBotGateway _gateway;

    public DiscordGuildDirectoryService(IApplicationDbContext db, IDiscordBotGateway gateway)
    {
        _db = db;
        _gateway = gateway;
    }

    public async Task<Result<DiscordGuildInfoDto>> GetGuildAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        string? guildId = await ResolveGuildIdAsync(broadcasterId, connectionId, ct);
        if (guildId is null)
            return Errors.NotFound<DiscordGuildInfoDto>(
                "Discord connection",
                connectionId.ToString()
            );
        return await _gateway.GetGuildAsync(broadcasterId, guildId, ct);
    }

    public async Task<Result<IReadOnlyList<DiscordGuildRoleDto>>> GetGuildRolesAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        string? guildId = await ResolveGuildIdAsync(broadcasterId, connectionId, ct);
        if (guildId is null)
            return Errors.NotFound<IReadOnlyList<DiscordGuildRoleDto>>(
                "Discord connection",
                connectionId.ToString()
            );
        return await _gateway.GetGuildRolesAsync(broadcasterId, guildId, ct);
    }

    public async Task<Result<IReadOnlyList<DiscordGuildChannelDto>>> GetGuildChannelsAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        string? guildId = await ResolveGuildIdAsync(broadcasterId, connectionId, ct);
        if (guildId is null)
            return Errors.NotFound<IReadOnlyList<DiscordGuildChannelDto>>(
                "Discord connection",
                connectionId.ToString()
            );
        return await _gateway.GetGuildChannelsAsync(broadcasterId, guildId, ct);
    }

    private async Task<string?> ResolveGuildIdAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct
    ) =>
        await _db
            .DiscordGuildConnections.Where(c =>
                c.Id == connectionId && c.BroadcasterId == broadcasterId
            )
            .Select(c => c.GuildId)
            .FirstOrDefaultAsync(ct);
}
