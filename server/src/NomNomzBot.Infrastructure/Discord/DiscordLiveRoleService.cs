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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.Entities;

namespace NomNomzBot.Infrastructure.Discord;

/// <inheritdoc cref="IDiscordLiveRoleService" />
public sealed class DiscordLiveRoleService : IDiscordLiveRoleService
{
    private readonly IApplicationDbContext _db;
    private readonly IDiscordGuildService _guildService;
    private readonly IDiscordBotGateway _gateway;
    private readonly ILogger<DiscordLiveRoleService> _logger;

    public DiscordLiveRoleService(
        IApplicationDbContext db,
        IDiscordGuildService guildService,
        IDiscordBotGateway gateway,
        ILogger<DiscordLiveRoleService> logger
    )
    {
        _db = db;
        _guildService = guildService;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task ApplyForOnlineAsync(
        Guid broadcasterId,
        string dedupeKey,
        CancellationToken ct = default
    )
    {
        List<DiscordLiveRoleConfig> configs = await _db
            .DiscordLiveRoleConfigs.Where(c => c.BroadcasterId == broadcasterId && c.Enabled)
            .ToListAsync(ct);

        foreach (DiscordLiveRoleConfig config in configs)
        {
            // Idempotent: a duplicate online event for the same stream session is a no-op, not a second call.
            if (config.IsCurrentlyApplied && config.AppliedDedupeKey == dedupeKey)
                continue;

            DiscordGuildConnection? connection = await FindActiveConnectionAsync(
                broadcasterId,
                config.GuildConnectionId,
                ct
            );
            if (connection is null)
            {
                // No accepted link (or it was revoked) — this is the tenant-isolation gate: a channel can
                // never drive a role in a guild it has no active both-opt-in link to.
                _logger.LogWarning(
                    "Discord live-role apply skipped for {BroadcasterId}: guild connection {ConnectionId} is not active.",
                    broadcasterId,
                    config.GuildConnectionId
                );
                continue;
            }

            Result validation = await _gateway.ValidateRoleAssignableAsync(
                broadcasterId,
                connection.GuildId,
                config.RoleId,
                ct
            );
            if (validation.IsFailure)
            {
                _logger.LogWarning(
                    "Discord live-role apply failed for {BroadcasterId} (guild {GuildId}, role {RoleId}): "
                        + "{ErrorCode} — {ErrorMessage}",
                    broadcasterId,
                    connection.GuildId,
                    config.RoleId,
                    validation.ErrorCode,
                    validation.ErrorMessage
                );
                continue;
            }

            Result added = await _gateway.AddMemberRoleAsync(
                broadcasterId,
                connection.GuildId,
                config.DiscordMemberId,
                config.RoleId,
                ct
            );
            if (added.IsFailure)
            {
                _logger.LogWarning(
                    "Discord live-role add failed for {BroadcasterId} (guild {GuildId}, role {RoleId}): "
                        + "{ErrorCode} — {ErrorMessage}",
                    broadcasterId,
                    connection.GuildId,
                    config.RoleId,
                    added.ErrorCode,
                    added.ErrorMessage
                );
                continue;
            }

            config.IsCurrentlyApplied = true;
            config.AppliedDedupeKey = dedupeKey;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveForOfflineAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        List<DiscordLiveRoleConfig> configs = await _db
            .DiscordLiveRoleConfigs.Where(c =>
                c.BroadcasterId == broadcasterId && c.IsCurrentlyApplied
            )
            .ToListAsync(ct);

        foreach (DiscordLiveRoleConfig config in configs)
            await RemoveOneAsync(config, ct);

        await _db.SaveChangesAsync(ct);
    }

    public async Task ReconcileStaleAsync(CancellationToken ct = default)
    {
        List<DiscordLiveRoleConfig> stale = await _db
            .DiscordLiveRoleConfigs.Where(c => c.IsCurrentlyApplied)
            .Join(
                _db.Channels,
                c => c.BroadcasterId,
                ch => ch.Id,
                (c, ch) => new { Config = c, ch.IsLive }
            )
            .Where(x => !x.IsLive)
            .Select(x => x.Config)
            .ToListAsync(ct);

        foreach (DiscordLiveRoleConfig config in stale)
        {
            _logger.LogWarning(
                "Discord live-role reconciliation: role {RoleId} was left applied for {BroadcasterId} "
                    + "with the channel offline — clearing it (missed offline event).",
                config.RoleId,
                config.BroadcasterId
            );
            await RemoveOneAsync(config, ct);
        }

        if (stale.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    private async Task RemoveOneAsync(DiscordLiveRoleConfig config, CancellationToken ct)
    {
        DiscordGuildConnection? connection = await _db.DiscordGuildConnections.FirstOrDefaultAsync(
            c => c.Id == config.GuildConnectionId && c.BroadcasterId == config.BroadcasterId,
            ct
        );
        if (connection is null)
        {
            // The link itself is gone (unlinked/disconnected) — nothing left to remove the role from via this
            // path; clear local state so we stop trying.
            config.IsCurrentlyApplied = false;
            config.AppliedDedupeKey = null;
            return;
        }

        Result removed = await _gateway.RemoveMemberRoleAsync(
            config.BroadcasterId,
            connection.GuildId,
            config.DiscordMemberId,
            config.RoleId,
            ct
        );
        if (removed.IsFailure && removed.ErrorCode != "DISCORD_NOT_FOUND")
        {
            _logger.LogWarning(
                "Discord live-role remove failed for {BroadcasterId} (guild {GuildId}, role {RoleId}): "
                    + "{ErrorCode} — {ErrorMessage}",
                config.BroadcasterId,
                connection.GuildId,
                config.RoleId,
                removed.ErrorCode,
                removed.ErrorMessage
            );
            // Leave IsCurrentlyApplied = true so the next offline/reconcile pass retries the removal.
            return;
        }

        config.IsCurrentlyApplied = false;
        config.AppliedDedupeKey = null;
    }

    private async Task<DiscordGuildConnection?> FindActiveConnectionAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct
    )
    {
        Result<bool> active = await _guildService.IsLinkActiveAsync(
            broadcasterId,
            connectionId,
            ct
        );
        if (active.IsFailure || !active.Value)
            return null;

        return await _db.DiscordGuildConnections.FirstOrDefaultAsync(
            c => c.Id == connectionId && c.BroadcasterId == broadcasterId,
            ct
        );
    }
}
