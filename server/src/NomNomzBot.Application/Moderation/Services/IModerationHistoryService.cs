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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Read + manual-note surface over the queryable <c>ModerationHistoryEntry</c> log (owner punch list
/// 2026-09-08 §3/§12) — the real per-action history the Community Profile page and a future Moderation
/// History page both consume. Rows themselves are written by <see cref="IModerationProjectionService.ApplyActionAsync"/>
/// for every enforcement action; this service is the read path, plus the one write path enforcement doesn't
/// cover — a moderator's free-text note.
/// </summary>
public interface IModerationHistoryService
{
    /// <summary>Paged, filterable, newest-first history for a channel (or one subject within it).</summary>
    Task<Result<PagedList<ModerationHistoryEntryDto>>> GetHistoryAsync(
        Guid broadcasterId,
        ModerationHistoryQuery query,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>
    /// Adds a manual, non-enforcement note about a subject — a moderator leaving context with no matching
    /// Twitch action. It is written to the shared user-note store (author, date, pin), not to the history log.
    /// NOT_FOUND if the subject has no local <c>User</c> row.
    /// </summary>
    Task<Result<UserNoteDto>> AddNoteAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        Guid moderatorUserId,
        string note,
        CancellationToken ct = default
    );
}
