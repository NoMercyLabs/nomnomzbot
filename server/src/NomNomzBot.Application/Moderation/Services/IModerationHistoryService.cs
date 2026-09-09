// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    /// Appends a manual, non-enforcement note (<see cref="Domain.Moderation.Entities.ModerationHistoryEntryKinds.Note"/>)
    /// to a subject's history — a moderator leaving context with no matching Twitch action. NOT_FOUND if the
    /// subject has no local <c>User</c> row.
    /// </summary>
    Task<Result<ModerationHistoryEntryDto>> AddNoteAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        Guid moderatorUserId,
        string note,
        CancellationToken ct = default
    );
}
