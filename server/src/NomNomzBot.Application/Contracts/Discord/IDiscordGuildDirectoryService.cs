// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Discord;

/// <summary>
/// Read-through directory of a linked guild for the dashboard's role/channel pickers (ROADMAP: guild read
/// endpoints instead of raw-id entry). Resolves the tenant's connection row (NOT_FOUND if absent or
/// other-tenant) and proxies the live guild surface through <see cref="IDiscordBotGateway"/> — nothing is
/// persisted; these are pure reads.
/// </summary>
public interface IDiscordGuildDirectoryService
{
    /// <summary>The linked guild's live profile (name, icon, description).</summary>
    Task<Result<DiscordGuildInfoDto>> GetGuildAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );

    /// <summary>The guild's live role list, for the notify-role picker.</summary>
    Task<Result<IReadOnlyList<DiscordGuildRoleDto>>> GetGuildRolesAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );

    /// <summary>The guild's live channel list, for the target/button channel pickers.</summary>
    Task<Result<IReadOnlyList<DiscordGuildChannelDto>>> GetGuildChannelsAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );

    /// <summary>
    /// The guild's live role list, each carrying whether the bot can assign it right now (S055c). Fails with
    /// <c>DISCORD_LINK_INACTIVE</c> when the guild link's both-opt-in handshake is not fully active — distinct
    /// from an empty role list and from a per-role "the bot can't use this one".
    /// </summary>
    Task<Result<IReadOnlyList<DiscordAssignableRoleDto>>> GetAssignableGuildRolesAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );

    /// <summary>
    /// The guild's live channel list, each carrying whether the bot can post in it right now (S055c). Fails
    /// with <c>DISCORD_LINK_INACTIVE</c> when the guild link's both-opt-in handshake is not fully active —
    /// distinct from an empty channel list and from a per-channel "the bot can't use this one".
    /// </summary>
    Task<Result<IReadOnlyList<DiscordPostableChannelDto>>> GetPostableGuildChannelsAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );
}
