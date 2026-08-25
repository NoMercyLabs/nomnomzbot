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
using NomNomzBot.Domain.Discord.Entities;

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

    public async Task<Result<IReadOnlyList<DiscordAssignableRoleDto>>> GetAssignableGuildRolesAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        Result<string> guildId = await ResolveActiveGuildIdAsync(broadcasterId, connectionId, ct);
        if (guildId.IsFailure)
            return Result.Failure<IReadOnlyList<DiscordAssignableRoleDto>>(
                guildId.ErrorMessage,
                guildId.ErrorCode,
                guildId.ErrorDetail
            );
        return await _gateway.GetAssignableGuildRolesAsync(broadcasterId, guildId.Value, ct);
    }

    public async Task<
        Result<IReadOnlyList<DiscordPostableChannelDto>>
    > GetPostableGuildChannelsAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        Result<string> guildId = await ResolveActiveGuildIdAsync(broadcasterId, connectionId, ct);
        if (guildId.IsFailure)
            return Result.Failure<IReadOnlyList<DiscordPostableChannelDto>>(
                guildId.ErrorMessage,
                guildId.ErrorCode,
                guildId.ErrorDetail
            );
        return await _gateway.GetPostableGuildChannelsAsync(broadcasterId, guildId.Value, ct);
    }

    /// <summary>
    /// Resolves the tenant's connection and its guild id, failing distinctly (<c>DISCORD_LINK_INACTIVE</c>)
    /// when the both-opt-in handshake is not fully active — the honest-unavailable state a picker must render
    /// differently from "empty" and from a per-item "the bot can't use this one" (S055c).
    /// </summary>
    private async Task<Result<string>> ResolveActiveGuildIdAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct
    )
    {
        DiscordGuildConnection? connection = await _db
            .DiscordGuildConnections.Where(c =>
                c.Id == connectionId && c.BroadcasterId == broadcasterId
            )
            .FirstOrDefaultAsync(ct);
        if (connection is null)
            return Errors.NotFound<string>("Discord connection", connectionId.ToString());

        if (connection.ServerConsentStatus != "approved" || !connection.StreamerEnabled)
            return Result.Failure<string>(
                "This Discord guild link is not active — both the server admin's consent and the "
                    + "streamer's toggle must be on before the bot can check role/channel permissions.",
                "DISCORD_LINK_INACTIVE"
            );

        return Result.Success(connection.GuildId);
    }
}
