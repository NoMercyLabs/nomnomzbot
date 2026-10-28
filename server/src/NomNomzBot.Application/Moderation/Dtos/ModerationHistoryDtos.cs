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

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>One row of the queryable <c>ModerationHistoryEntry</c> log.</summary>
public sealed record ModerationHistoryEntryDto(
    Guid Id,
    Guid SubjectUserId,
    string SubjectTwitchUserId,
    string ActionType,
    Guid? ModeratorUserId,
    string? ModeratorDisplayName,
    string? Reason,
    int? DurationSeconds,
    DateTime OccurredAt,
    DateTime? AcknowledgedAt = null
);

/// <summary>
/// Filter for <c>IModerationHistoryService.GetHistoryAsync</c> — every field is optional; an all-null query
/// returns the whole channel log (the Moderation History page's default view), while a populated
/// <see cref="SubjectUserId"/> narrows to one person's log (the Community Profile page's "full log" link).
/// </summary>
public sealed record ModerationHistoryQuery(
    Guid? SubjectUserId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? ActionType = null
);
