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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

public class Channel : SoftDeletableEntity
{
    // Surrogate UUIDv7 PK (schema §1.1, A.2) = the tenant id used in every BroadcasterId + RLS.
    // Generated app-side; never DB-default; never sent to Twitch.
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // Broadcaster identity — one channel per owner (schema A.2). Replaces the old
    // [ForeignKey(nameof(Id))] shared-PK hack between Channel and User.
    public Guid OwnerUserId { get; set; }

    // External Twitch channel/broadcaster id — first-class indexed attribute (schema A.2),
    // NOT the key. Every Helix call's broadcaster_id resolves to this from the tenant Guid.
    [MaxLength(50)]
    public string TwitchChannelId { get; set; } = null!;

    [MaxLength(25)]
    public string Name { get; set; } = null!;

    // Lower-cased Name for case-insensitive lookup (IRC keys by login name, schema A.2).
    [MaxLength(25)]
    public string NameNormalized { get; set; } = null!;

    // Tenant lifecycle ([VC:enum], schema A.2).
    [MaxLength(20)]
    public string Status { get; set; } = AuthEnums.ChannelStatus.Active;

    public DateTime? SuspendedAt { get; set; }

    [MaxLength(500)]
    public string? SuspendedReason { get; set; }

    // Deployment profile this tenant runs under ([VC:enum], schema A.2).
    [MaxLength(20)]
    public string DeploymentMode { get; set; } = AuthEnums.DeploymentMode.Saas;

    // Resolved billing tier key (cross-ref monetization.md); the entitlement source for gated features.
    [MaxLength(20)]
    public string BillingTierKey { get; set; } = "free";

    public bool Enabled { get; set; } = true;

    [MaxLength(450)]
    public string? ShoutoutTemplate { get; set; }

    public DateTime? LastShoutout { get; set; }

    public int ShoutoutInterval { get; set; } = 10;

    [MaxLength(100)]
    public string? UsernamePronunciation { get; set; }

    public bool IsOnboarded { get; set; }

    public DateTime? BotJoinedAt { get; set; }

    [MaxLength(36)]
    public string OverlayToken { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Opaque, rotatable public song-request-page token (music-sr.md §3.7) — resolves the public
    /// <c>/sr/{token}</c> page to this channel without a JWT. Distinct from <see cref="OverlayToken"/> (OBS
    /// sources). Null until first minted by <c>ISongRequestPageTokenService.GetOrCreateAsync</c>. Not PII.
    /// </summary>
    [MaxLength(64)]
    public string? SongRequestPageToken { get; set; }

    public bool IsLive { get; set; }

    [MaxLength(50)]
    public string? Language { get; set; }

    [MaxLength(50)]
    public string? GameId { get; set; }

    [MaxLength(255)]
    public string? GameName { get; set; }

    [MaxLength(255)]
    public string? Title { get; set; }

    public int StreamDelay { get; set; }

    public List<string> Tags { get; set; } = [];
    public List<string> ContentLabels { get; set; } = [];

    public bool IsBrandedContent { get; set; }

    [ForeignKey(nameof(OwnerUserId))]
    public virtual User User { get; set; } = null!;

    public virtual ICollection<ChannelModerator> Moderators { get; set; } = [];
    public virtual ICollection<global::NomNomzBot.Domain.Stream.Entities.Stream> Streams { get; set; } =
    [];
    public virtual ICollection<ChannelEvent> Events { get; set; } = [];
}
