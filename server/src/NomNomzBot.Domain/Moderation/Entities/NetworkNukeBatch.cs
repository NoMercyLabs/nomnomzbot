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
/// One SuperMod network-nuke fan-out (moderation.md §3.4, schema J.2a) — the batch a cross-channel mass
/// ban belongs to and the handle its one-shot reversal (un-nuke) targets. Deliberately NOT tenant-filtered:
/// the batch spans channels; each per-channel leg is its own <c>moderation_action</c> record carrying
/// <c>NetworkNukeBatchId</c>. <c>Status</c>: <c>active</c> | <c>partial</c> (some legs failed) |
/// <c>reverted</c>.
/// </summary>
public class NetworkNukeBatch : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The channel the nuke was initiated FROM (list scope).</summary>
    public Guid OriginBroadcasterId { get; set; }

    public Guid? InitiatedByUserId { get; set; }

    /// <summary>The chat term that triggered the nuke, when term-driven (metadata, PII-scrubbed).</summary>
    [MaxLength(500)]
    public string? MatchTerm { get; set; }

    public Guid? TargetUserId { get; set; }

    [MaxLength(50)]
    public string? TargetTwitchUserId { get; set; }

    /// <summary>Channels actually actioned (successful legs).</summary>
    public int ChannelCount { get; set; }

    /// <summary><c>active</c> | <c>partial</c> | <c>reverted</c> [VC:enum].</summary>
    [MaxLength(20)]
    public string Status { get; set; } = NetworkNukeStatus.Active;

    public Guid? RevertedByUserId { get; set; }

    public DateTime? RevertedAt { get; set; }
}

/// <summary>The closed J.2a status vocabulary.</summary>
public static class NetworkNukeStatus
{
    public const string Active = "active";
    public const string Partial = "partial";
    public const string Reverted = "reverted";
}
