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
