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

namespace NomNomzBot.Domain.MediaShare.Entities;

/// <summary>Closed source set for a media-share submission (media-share.md D2).</summary>
public static class MediaShareSourceType
{
    public const string TwitchClip = "twitch_clip";
    public const string YouTube = "youtube";
}

/// <summary>Lifecycle of a media-share request (media-share.md L.11).</summary>
public static class MediaShareStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Playing = "playing";
    public const string Played = "played";
    public const string Skipped = "skipped";
}

/// <summary>
/// One viewer-submitted clip/video in the media-share queue (media-share.md L.11). Approved items play in
/// <see cref="QueuePosition"/> order on the overlay.
/// </summary>
public class MediaShareRequest : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    public Guid RequesterUserId { get; set; }

    /// <summary>PII-hash of the requester's platform id (never the raw id).</summary>
    [MaxLength(64)]
    public string RequesterTwitchUserId { get; set; } = null!;

    /// <summary><see cref="MediaShareSourceType"/>.</summary>
    [MaxLength(20)]
    public string SourceType { get; set; } = null!;

    [MaxLength(2048)]
    public string SourceUrl { get; set; } = null!;

    /// <summary>Clip slug or YouTube video id.</summary>
    [MaxLength(255)]
    public string MediaRef { get; set; } = null!;

    [MaxLength(300)]
    public string? Title { get; set; }

    public int DurationSeconds { get; set; }

    [MaxLength(2048)]
    public string? ThumbnailUrl { get; set; }

    /// <summary><see cref="MediaShareStatus"/>.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = MediaShareStatus.Pending;

    /// <summary>Play order among approved items; null until approved.</summary>
    public int? QueuePosition { get; set; }

    /// <summary>The entry-cost debit ledger row, so a reject/skip can refund it.</summary>
    public long? EntryCostLedgerEntryId { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public Guid? DecidedByUserId { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(RequesterUserId))]
    public virtual User Requester { get; set; } = null!;
}
