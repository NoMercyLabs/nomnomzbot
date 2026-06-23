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
/// The Discord REST adapter — the ONLY thing that talks to Discord (discord.md §3.5). Every method resolves the
/// tenant's decrypted bot token per call (the discord <c>IntegrationConnection</c> → vault), never a cached
/// plaintext token; a crypto-shredded DEK surfaces as <c>Result.Failure</c>. Never throws for a Discord-side
/// failure.
/// </summary>
public interface IDiscordBotGateway
{
    /// <summary>
    /// Posts a channel message (optionally embed + role ping) with the tenant's decrypted bot token. Returns
    /// the Discord message id; failure → <c>Result.Failure</c>.
    /// </summary>
    Task<Result<string>> PostMessageAsync(
        Guid broadcasterId,
        string targetChannelId,
        DiscordOutboundMessage message,
        CancellationToken ct = default
    );

    /// <summary>Posts the role self-assign button message; returns its message id.</summary>
    Task<Result<string>> PostButtonMessageAsync(
        Guid broadcasterId,
        string targetChannelId,
        DiscordOptInButton button,
        CancellationToken ct = default
    );

    /// <summary>Adds a guild role on a member (opt-in enforcement).</summary>
    Task<Result> AddMemberRoleAsync(
        Guid broadcasterId,
        string guildId,
        string discordMemberId,
        string discordRoleId,
        CancellationToken ct = default
    );

    /// <summary>Removes a guild role on a member (opt-out enforcement).</summary>
    Task<Result> RemoveMemberRoleAsync(
        Guid broadcasterId,
        string guildId,
        string discordMemberId,
        string discordRoleId,
        CancellationToken ct = default
    );
}
