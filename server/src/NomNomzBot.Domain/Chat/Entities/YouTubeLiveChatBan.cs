// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Chat.Entities;

/// <summary>
/// The ban-id ledger for YouTube live-chat moderation (BUILD slice 3b): <c>liveChatBans.delete</c> (unban)
/// only accepts the ban resource id the <c>liveChatBans.insert</c> response returned, so every ban/timeout
/// the bot issues records its id here. Persisted (not in-memory) because a permanent ban outlives both the
/// live session and the process — an unban weeks later must still find the id. A consumed row (unban issued,
/// or YouTube reports the ban already gone) is soft-deleted; the newest live row per viewer is the one an
/// unban consumes.
/// </summary>
public class YouTubeLiveChatBan : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning tenant — the platform Channel the moderation ran against.</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>
    /// The PRIMARY channel whose Google OAuth token issued the ban — recorded so an unban resolves the same
    /// token later, including OFFLINE (a permanent ban outlives the live session, and without a live session
    /// there is no registry entry to map the tenant back to its token owner).
    /// </summary>
    public Guid PrimaryBroadcasterId { get; set; }

    /// <summary>The live chat the ban was issued in (bans are scoped to a chat at the API).</summary>
    public string LiveChatId { get; set; } = null!;

    /// <summary>The banned viewer's YouTube channel id (the seam's per-platform user id).</summary>
    public string BannedChannelId { get; set; } = null!;

    /// <summary>The ban resource id from the <c>liveChatBans.insert</c> response — the delete key.</summary>
    public string BanId { get; set; } = null!;

    /// <summary>The ban type at the API — <c>temporary</c> (timeout) or <c>permanent</c>.</summary>
    public string BanType { get; set; } = null!;

    /// <summary>The timeout length for a temporary ban; null for a permanent one.</summary>
    public int? DurationSeconds { get; set; }
}
