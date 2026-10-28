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
/// The per-viewer moderation rollup (moderation.md §3.8, schema J.4) — a PROJECTION, incrementally
/// maintained by the moderation event handlers and fully rebuildable from the recorded actions. One row
/// per (channel, subject).
/// </summary>
public class UserModerationHistory : BaseEntity, ITenantScoped
{
    public long Id { get; set; }

    public Guid BroadcasterId { get; set; }

    public Guid SubjectUserId { get; set; }

    /// <summary>The subject's Twitch id — the key the live events carry [PII-hash].</summary>
    [MaxLength(50)]
    public string SubjectTwitchUserId { get; set; } = null!;

    public int TimeoutCount { get; set; }

    public int BanCount { get; set; }

    public int WarningCount { get; set; }

    public int MessagesDeletedCount { get; set; }

    public DateTime? LastActionAt { get; set; }

    /// <summary><c>ban</c> | <c>unban</c> | <c>timeout</c> | <c>warn</c> | <c>delete_message</c> … [VC:enum].</summary>
    [MaxLength(20)]
    public string? LastActionType { get; set; }

    public DateTime? FirstSeenAt { get; set; }
}
