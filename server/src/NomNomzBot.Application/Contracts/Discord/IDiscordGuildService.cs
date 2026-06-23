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
/// The both-opt-in handshake + connection lifecycle (discord.md §3.1). Owns the
/// <c>DiscordGuildConnection</c> row and the bot OAuth token's vault custody (via <c>IIntegrationTokenVault</c>);
/// never stores a plaintext token. <c>BroadcasterId</c> is the tenant <c>Guid</c> throughout.
/// </summary>
public interface IDiscordGuildService
{
    /// <summary>Lists every guild link for the tenant (any consent state). Read-only.</summary>
    Task<Result<IReadOnlyList<DiscordGuildConnectionDto>>> GetConnectionsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Single connection by id, tenant-scoped. <c>NOT_FOUND</c> if absent or other-tenant.</summary>
    Task<Result<DiscordGuildConnectionDto>> GetConnectionAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Upserts the connection after the Discord bot-install OAuth callback: creates/updates the
    /// (BroadcasterId, GuildId) row, sets <c>BotInstalled=true</c> + <c>ServerConsentStatus=approved</c>, and
    /// vaults the bot OAuth token (UpsertConnectionAsync → StoreTokensAsync). Publishes
    /// <c>DiscordGuildLinkedEvent</c> if this completes both-opt-in. Idempotent.
    /// </summary>
    Task<Result<DiscordGuildConnectionDto>> UpsertFromOAuthAsync(
        Guid broadcasterId,
        DiscordGuildOAuthResult oauth,
        CancellationToken ct = default
    );

    /// <summary>
    /// Server-admin approve. Sets <c>ServerConsentStatus=approved</c> + approver + timestamp; if the streamer
    /// already enabled it, reaches both-opt-in and publishes <c>DiscordGuildLinkedEvent</c>.
    /// </summary>
    Task<Result> ApproveServerConsentAsync(
        Guid broadcasterId,
        Guid connectionId,
        string approvedByDiscordUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Server-admin revoke. Sets <c>ServerConsentStatus=revoked</c>; breaks both-opt-in and publishes
    /// <c>DiscordGuildUnlinkedEvent("server_revoked")</c>.
    /// </summary>
    Task<Result> RevokeServerConsentAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Streamer side of consent (the dashboard toggle). <c>true</c> + server approved → both-opt-in →
    /// <c>DiscordGuildLinkedEvent</c>; <c>false</c> → <c>DiscordGuildUnlinkedEvent("streamer_disabled")</c>.
    /// </summary>
    Task<Result> SetStreamerEnabledAsync(
        Guid broadcasterId,
        Guid connectionId,
        bool enabled,
        CancellationToken ct = default
    );

    /// <summary>
    /// Full disconnect: soft-deletes the connection + its configs/roles, revokes the vaulted bot token, and
    /// publishes <c>DiscordGuildUnlinkedEvent("disconnected")</c>. Idempotent.
    /// </summary>
    Task<Result> DisconnectAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );

    /// <summary>True iff server approved AND streamer enabled AND not soft-deleted — the dispatcher's single gate.</summary>
    Task<Result<bool>> IsLinkActiveAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );
}
