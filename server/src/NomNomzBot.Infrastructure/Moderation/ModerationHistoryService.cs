// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The read + manual-note implementation of <see cref="IModerationHistoryService"/>: reads the queryable
/// <see cref="ModerationHistoryEntry"/> log; a manual note is written through the one surviving note store
/// (<see cref="IModerationService.AddUserNoteAsync"/>), never as a history row.
/// </summary>
public sealed class ModerationHistoryService(
    IApplicationDbContext db,
    IModerationService moderation
) : IModerationHistoryService
{
    public async Task<Result<PagedList<ModerationHistoryEntryDto>>> GetHistoryAsync(
        Guid broadcasterId,
        ModerationHistoryQuery query,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<ModerationHistoryEntry> filtered = db.ModerationHistoryEntries.Where(e =>
            e.BroadcasterId == broadcasterId
        );

        if (query.SubjectUserId is { } subjectUserId)
            filtered = filtered.Where(e => e.SubjectUserId == subjectUserId);

        if (query.FromUtc is { } from)
            filtered = filtered.Where(e => e.OccurredAt >= from);

        if (query.ToUtc is { } to)
            filtered = filtered.Where(e => e.OccurredAt <= to);

        if (!string.IsNullOrWhiteSpace(query.ActionType))
            filtered = filtered.Where(e => e.ActionType == query.ActionType);

        int total = await filtered.CountAsync(ct);

        List<ModerationHistoryEntryDto> items = await filtered
            .OrderByDescending(e => e.OccurredAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(e => new ModerationHistoryEntryDto(
                e.Id,
                e.SubjectUserId,
                e.SubjectTwitchUserId,
                e.ActionType,
                e.ModeratorUserId,
                e.ModeratorDisplayName,
                e.Reason,
                e.DurationSeconds,
                e.OccurredAt
            ))
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<ModerationHistoryEntryDto>(
                items,
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<UserNoteDto>> AddNoteAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        Guid moderatorUserId,
        string note,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(note))
            return Errors.ValidationFailed("A moderation note needs text.").ToTyped<UserNoteDto>();

        User? subject = await db.Users.FirstOrDefaultAsync(u => u.Id == subjectUserId, ct);
        if (subject?.TwitchUserId is null)
            return Errors.NotFound<UserNoteDto>("User", subjectUserId.ToString());

        return await moderation.AddUserNoteAsync(
            broadcasterId.ToString(),
            subject.TwitchUserId,
            new CreateUserNoteRequest { Content = note },
            moderatorUserId.ToString(),
            ct
        );
    }
}
