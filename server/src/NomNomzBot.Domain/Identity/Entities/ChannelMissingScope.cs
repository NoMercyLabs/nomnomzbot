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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// A Twitch scope a channel's streamer token was found to be missing at runtime (identity-auth §3.4a) — the
/// reactive complement to the proactive <c>FeatureScopeMap</c> diff. A row is recorded the first time a Helix
/// read short-circuits or fails with <c>missing_scope</c> for this <c>(channel, scope)</c> pair, so the missing
/// permission is surfaced (dashboard banner + one chat notice) instead of silently degrading the feature.
/// <para>
/// The row is the idempotency anchor for the chat notice: <see cref="ChatNotifiedAt"/> is stamped exactly once
/// when the bot posts the "I need '&lt;scope&gt;'" message, so the notice fires once per missing-scope-set and
/// never spams. The row is removed when a later grant reconciliation restores the scope (the banner clears and
/// a future loss is re-notified afresh).
/// </para>
/// </summary>
public class ChannelMissingScope : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The channel (tenant) whose streamer token is missing the scope.</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>The absent Twitch scope (e.g. <c>moderator:read:followers</c>) — a <c>TwitchScopes</c> value.</summary>
    [MaxLength(100)]
    public string Scope { get; set; } = null!;

    /// <summary>
    /// The feature key the missing scope blocks (a <c>FeatureScopeMap</c> key, e.g. <c>followers</c>), or null
    /// when the scope was detected from a raw Helix failure not tied to a known feature.
    /// </summary>
    [MaxLength(50)]
    public string? Feature { get; set; }

    /// <summary>When the missing scope was first observed for this channel (set once, on insert).</summary>
    public DateTime DetectedAt { get; set; }

    /// <summary>When the streamer was last told about this missing scope in chat — null until the notice posts.</summary>
    public DateTime? ChatNotifiedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
