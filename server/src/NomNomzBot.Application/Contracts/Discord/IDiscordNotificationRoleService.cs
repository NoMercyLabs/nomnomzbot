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
/// Self-assign notify roles + member opt-in management (discord.md §3.3). Opt-in/out push the Discord role via
/// <c>IDiscordBotGateway</c> and publish <c>DiscordMemberOptInChangedEvent</c>.
/// </summary>
public interface IDiscordNotificationRoleService
{
    /// <summary>All notify roles for a connection (with live opt-in counts). Read-only.</summary>
    Task<Result<IReadOnlyList<DiscordNotificationRoleDto>>> GetRolesAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Registers a Discord role as the per-streamer notify role. <c>ALREADY_EXISTS</c> if
    /// (GuildConnectionId, DiscordRoleId) exists. Does NOT post the button.
    /// </summary>
    Task<Result<DiscordNotificationRoleDto>> CreateRoleAsync(
        Guid broadcasterId,
        Guid connectionId,
        CreateDiscordNotificationRoleRequest request,
        CancellationToken ct = default
    );

    /// <summary>Updates <c>RoleName</c> / <c>SelfAssignEnabled</c>. <c>NOT_FOUND</c> if absent.</summary>
    Task<Result<DiscordNotificationRoleDto>> UpdateRoleAsync(
        Guid broadcasterId,
        Guid roleId,
        UpdateDiscordNotificationRoleRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Soft-deletes the notify role; configs referencing it via <c>PingRoleId</c> are nulled in the same
    /// transaction. <c>NOT_FOUND</c> if absent.
    /// </summary>
    Task<Result> DeleteRoleAsync(Guid broadcasterId, Guid roleId, CancellationToken ct = default);

    /// <summary>
    /// Posts (or re-posts) the bot opt-in button to <paramref name="buttonChannelId"/> via the gateway; records
    /// the returned <c>ButtonMessageId</c> on the role row.
    /// </summary>
    Task<Result<DiscordNotificationRoleDto>> PostOptInButtonAsync(
        Guid broadcasterId,
        Guid roleId,
        string buttonChannelId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Records a member opt-in (idempotent on (role, member)); assigns the Discord role via the gateway;
    /// publishes <c>DiscordMemberOptInChangedEvent(true)</c>.
    /// </summary>
    Task<Result> OptInMemberAsync(
        Guid broadcasterId,
        Guid roleId,
        string discordMemberId,
        string source,
        CancellationToken ct = default
    );

    /// <summary>
    /// Records an opt-out; removes the Discord role via the gateway; publishes
    /// <c>DiscordMemberOptInChangedEvent(false)</c>. <c>NOT_FOUND</c> if no opt-in row.
    /// </summary>
    Task<Result> OptOutMemberAsync(
        Guid broadcasterId,
        Guid roleId,
        string discordMemberId,
        string source,
        CancellationToken ct = default
    );
}
