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
/// The append-only dispatch + dedupe log (schema P.10). The unique <c>(NotificationConfigId, DedupeKey)</c>
/// index IS the dedupe mechanism — a duplicate insert is the "already posted" signal (one post per go-live).
/// APPEND-ONLY: <c>Id</c> is UUIDv7 app-assigned, only <see cref="CreatedAt"/> is carried (no
/// <c>UpdatedAt</c>/<c>DeletedAt</c>); tenant-scoped (the filter applies). The outcome (<see cref="Status"/>,
/// <see cref="PostedMessageId"/>, <see cref="Error"/>) is written once on the same row after the post.
/// </summary>
public class DiscordNotificationDispatch : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    public Guid NotificationConfigId { get; set; }

    [MaxLength(30)]
    public string TriggerType { get; set; } = null!;

    /// <summary>The per-trigger dedupe key; the unique index over (config, key) is the dedupe guarantee.</summary>
    [MaxLength(255)]
    public string DedupeKey { get; set; } = null!;

    public Guid? StreamId { get; set; }

    /// <summary>The Discord message id returned by the post (null on failure/dupe).</summary>
    [MaxLength(50)]
    public string? PostedMessageId { get; set; }

    /// <summary><c>sent</c> | <c>failed</c> | <c>skipped_dupe</c> [VC:enum].</summary>
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    public string? Error { get; set; }

    public DateTime DispatchedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(NotificationConfigId))]
    public virtual DiscordNotificationConfig NotificationConfig { get; set; } = null!;
}
