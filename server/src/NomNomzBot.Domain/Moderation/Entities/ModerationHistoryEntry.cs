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
/// The append-only, per-action moderation log (owner punch list 2026-09-08 §3) — the queryable history a
/// Community Profile page and a future Moderation History page both need. <see cref="UserModerationHistory"/>
/// (J.4) stays the incrementally-maintained ROLLUP (counts + last action, rebuildable); this table is the
/// individual events behind that rollup, one row per moderation action, filterable by person and date range
/// and paged like every other list in this codebase (<see cref="Application.Common.Models.PaginationParams"/>).
/// <para>
/// Written from the SAME single choke point every ban/timeout/unban/warn already funnels through —
/// <c>IModerationProjectionService.ApplyActionAsync</c> — so it captures every source (bot-issued via
/// <c>ModerationService</c>, a mod acting directly on Twitch, Kick webhook ingest, legacy-bot import) the
/// exact same way the J.4 rollup already does, never a second parallel write path.
/// </para>
/// </summary>
public class ModerationHistoryEntry : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>The subject (target) of the action — resolved the same way J.4 resolves it.</summary>
    public Guid SubjectUserId { get; set; }

    /// <summary>The subject's Twitch id — the key the live events carry [PII-hash].</summary>
    [MaxLength(50)]
    public string SubjectTwitchUserId { get; set; } = null!;

    /// <summary>One of <see cref="ModerationHistoryEntryKinds"/>.</summary>
    [MaxLength(20)]
    public string ActionType { get; set; } = null!;

    /// <summary>The acting moderator's local User row, when resolvable. Null for a system/automated action
    /// (AutoMod, an unattributed EventSub fact) or a moderator with no local User row yet.</summary>
    public Guid? ModeratorUserId { get; set; }

    /// <summary>The acting moderator's Twitch id, when the source event carried one.</summary>
    [MaxLength(50)]
    public string? ModeratorTwitchUserId { get; set; }

    /// <summary>The acting moderator's display name at the time of the action (denormalized for display
    /// without a join — mirrors <see cref="Stream.Entities.ShoutoutOverride.TargetDisplayName"/>'s reasoning).</summary>
    [MaxLength(100)]
    public string? ModeratorDisplayName { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>Timeout duration in seconds; null for every other <see cref="ActionType"/>.</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>When the action actually happened (the source event's timestamp, not row-insert time).</summary>
    public DateTime OccurredAt { get; set; }
}

/// <summary>
/// The closed set of history-entry kinds — the same action-type vocabulary <c>IModerationProjectionService</c>
/// already uses for the J.4 rollup (<c>ban</c> | <c>timeout</c> | <c>warn</c> | <c>unban</c> |
/// <c>delete_message</c> | <c>automod_denied</c> | <c>filter_hit</c> | <c>report_validated</c>), plus
/// <see cref="Note"/> — a manual, non-enforcement entry a moderator adds directly (no matching enforcement
/// event exists for this one; it is written straight through <c>AddNoteAsync</c>, not <c>ApplyActionAsync</c>).
/// </summary>
public static class ModerationHistoryEntryKinds
{
    public const string Ban = "ban";
    public const string Timeout = "timeout";
    public const string Warn = "warn";
    public const string Unban = "unban";
    public const string DeleteMessage = "delete_message";
    public const string AutoModDenied = "automod_denied";
    public const string FilterHit = "filter_hit";
    public const string ReportValidated = "report_validated";
    public const string Note = "note";

    public static bool IsKnown(string? kind) =>
        kind
            is Ban
                or Timeout
                or Warn
                or Unban
                or DeleteMessage
                or AutoModDenied
                or FilterHit
                or ReportValidated
                or Note;

    public static IReadOnlyList<string> All { get; } =
    [Ban, Timeout, Warn, Unban, DeleteMessage, AutoModDenied, FilterHit, ReportValidated, Note];
}
