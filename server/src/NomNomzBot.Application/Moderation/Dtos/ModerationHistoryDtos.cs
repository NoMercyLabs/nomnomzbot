// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    DateTime OccurredAt
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
