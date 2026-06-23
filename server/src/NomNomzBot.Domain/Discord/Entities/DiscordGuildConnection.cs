// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Discord.Entities;

/// <summary>
/// The both-opt-in Discord guild link (schema P.10) — supersedes the legacy <c>DiscordServerAuthorization</c>.
/// A link is active only when the server admin approved (<see cref="ServerConsentStatus"/> = <c>approved</c>)
/// AND the streamer enabled it (<see cref="StreamerEnabled"/>). The bot OAuth token is NOT a column here: it
/// lives crypto-vaulted in an <c>IntegrationConnection</c>/<c>IntegrationToken</c> pair (Provider=<c>discord</c>,
/// ProviderAccountId=<see cref="GuildId"/>), resolved by the vault's natural key — so no token handle is stored
/// on this row.
/// </summary>
public class DiscordGuildConnection : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>The external Discord guild (server) snowflake id — an indexed attribute, never a key.</summary>
    [MaxLength(50)]
    public string GuildId { get; set; } = null!;

    /// <summary>Display name of the guild [PII-scrub].</summary>
    [MaxLength(255)]
    public string? GuildName { get; set; }

    /// <summary>True once the bot has been installed into the guild via the OAuth bot-install flow.</summary>
    public bool BotInstalled { get; set; }

    /// <summary>Server-admin consent side: <c>pending</c> | <c>approved</c> | <c>revoked</c> [VC:enum].</summary>
    [MaxLength(20)]
    public string ServerConsentStatus { get; set; } = "pending";

    /// <summary>The Discord user id of the server admin who approved [PII-hash].</summary>
    [MaxLength(50)]
    public string? ApprovedByDiscordUserId { get; set; }

    public DateTime? ApprovedAt { get; set; }

    /// <summary>Streamer consent side: the dashboard toggle. Both sides must be true for an active link.</summary>
    public bool StreamerEnabled { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
