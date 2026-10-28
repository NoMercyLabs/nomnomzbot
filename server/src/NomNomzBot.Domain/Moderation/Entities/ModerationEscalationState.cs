// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// One viewer's position on the channel's escalation ladder (moderation.md §3.11, schema J.11) —
/// the per-window offense tally. Cleared by moderator forgiveness; restarted when the window lapses.
/// Unique per (channel, subject).
/// </summary>
public class ModerationEscalationState : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    public Guid SubjectUserId { get; set; }

    [MaxLength(50)]
    public string SubjectTwitchUserId { get; set; } = null!;

    /// <summary>Offenses inside the current window (clamps to the ladder's highest step).</summary>
    public int OffenseCount { get; set; }

    public DateTime WindowStartedAt { get; set; }

    public DateTime LastOffenseAt { get; set; }
}
