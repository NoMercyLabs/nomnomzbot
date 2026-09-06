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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// One network-wide block (S-ADMIN-8b): the most dangerous control in the product, because it acts across
/// EVERY tenant of this deployment at once rather than one channel. Deliberately NOT tenant-filtered — a
/// single row names the actor, the operator who applied it, and (once fully undone) the operator who
/// lifted it. <see cref="TenantCount"/> is the blast radius the operator actually confirmed before this
/// row was created (the fail-closed preview count); <see cref="ChannelCount"/> is how many of those
/// tenants were actually actioned. The enforcement gate (<c>RoleResolver.HasCapabilityAsync</c>) denies
/// every Gate-2 action network-wide for as long as <see cref="Status"/> is <c>active</c> or <c>partial</c> —
/// including in a tenant the actor was never individually blocked in.
/// </summary>
public class NetworkBlock : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The internal user row the block enforces against — the same identity RoleResolver keys on.</summary>
    public Guid TargetUserId { get; set; }

    [MaxLength(50)]
    public string TargetTwitchUserId { get; set; } = null!;

    [MaxLength(100)]
    public string? TargetDisplayName { get; set; }

    /// <summary>Why the actor was blocked (public-facing, shown on the record).</summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>The operator's own justification for reaching for the network-wide control (audit trail).</summary>
    [MaxLength(1000)]
    public string Justification { get; set; } = null!;

    public Guid AppliedByPrincipalId { get; set; }

    public DateTime AppliedAt { get; set; }

    /// <summary>The confirmed preview count the apply call was gated on — the blast radius the operator saw.</summary>
    public int TenantCount { get; set; }

    /// <summary>Tenants actually actioned (successful Twitch ban legs) at apply time.</summary>
    public int ChannelCount { get; set; }

    /// <summary><c>active</c> | <c>partial</c> | <c>lifted</c> [VC:enum].</summary>
    [MaxLength(20)]
    public string Status { get; set; } = NetworkBlockStatus.Active;

    /// <summary>Set on every lift ATTEMPT, whether or not it fully succeeded — names who tried and why.</summary>
    public Guid? LiftedByPrincipalId { get; set; }

    [MaxLength(1000)]
    public string? LiftJustification { get; set; }

    public DateTime? LiftAttemptedAt { get; set; }

    /// <summary>
    /// Stamped ONLY when every tenant leg was actually unbanned — the same reversal law as
    /// <c>SpamCampaignRecord.ReversedAt</c>: a partial outcome never claims a clean lift.
    /// </summary>
    public DateTime? LiftedAt { get; set; }

    /// <summary>Tenants actually restored on the most recent lift attempt.</summary>
    public int RestoredChannelCount { get; set; }

    /// <summary>Comma-joined channel ids the most recent lift attempt could not restore — empty when none.</summary>
    [MaxLength(2000)]
    public string LiftFailedChannelIds { get; set; } = string.Empty;
}

/// <summary>The closed network-block status vocabulary.</summary>
public static class NetworkBlockStatus
{
    public const string Active = "active";
    public const string Partial = "partial";
    public const string Lifted = "lifted";
}
