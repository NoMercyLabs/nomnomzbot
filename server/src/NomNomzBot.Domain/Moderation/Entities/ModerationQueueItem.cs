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
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// A message or user held for moderator review (moderation.md J.1). This slice covers the
/// <see cref="ModerationQueueSource.AutoMod"/> path only: <c>automod.message.hold</c> enqueues a
/// row (<see cref="Status"/> = <see cref="ModerationQueueStatus.Pending"/>); resolving it (approve/deny) relays
/// through Helix <c>POST /moderation/automod/message</c> and stamps <see cref="ResolvedByUserId"/> /
/// <see cref="ResolvedAt"/> / <see cref="ResolutionAction"/>. Twitch also reports resolutions made outside the
/// dashboard (another mod, or Twitch auto-expiry) via <c>automod.message.update</c> — those close the row with
/// <see cref="ResolvedByUserId"/> left null. <see cref="AutoModMessageId"/> is the raw Twitch message id the
/// held-message Helix call addresses directly; unlike the spec's <c>ChatMessageId</c> FK, a held message may
/// never be persisted as a <c>ChatMessage</c> row (AutoMod holds it before it reaches chat), so this stores the
/// Twitch id as a plain string snapshot instead of a foreign key.
/// </summary>
public class ModerationQueueItem : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The owning channel (tenant key).</summary>
    public Guid BroadcasterId { get; set; }

    public ModerationQueueSource Source { get; set; }

    public ModerationQueueStatus Status { get; set; } = ModerationQueueStatus.Pending;

    /// <summary>The resolved internal user id of the message's sender, when it could be resolved.</summary>
    public Guid? TargetUserId { get; set; }

    [MaxLength(50)]
    public string? TargetTwitchUserId { get; set; }

    [MaxLength(50)]
    public string? TargetUsernameSnapshot { get; set; }

    /// <summary>The raw Twitch message id AutoMod held — what the resolve call addresses.</summary>
    [MaxLength(100)]
    public string? AutoModMessageId { get; set; }

    [MaxLength(500)]
    public string? MessageContentSnapshot { get; set; }

    /// <summary>The AutoMod classifier category (e.g. <c>swearing</c>, <c>aggression</c>) for a source=AutoMod row.</summary>
    [MaxLength(50)]
    public string? AutoModCategory { get; set; }

    /// <summary>Set only when a dashboard moderator resolved it; null when Twitch reported an external resolution.</summary>
    public Guid? ResolvedByUserId { get; set; }

    public DateTime? ResolvedAt { get; set; }

    /// <summary>The raw resolution verdict (<c>approved</c> / <c>denied</c> / <c>expired</c>).</summary>
    [MaxLength(20)]
    public string? ResolutionAction { get; set; }
}
