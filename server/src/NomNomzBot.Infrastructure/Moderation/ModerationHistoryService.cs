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
/// The read + manual-note implementation of <see cref="IModerationHistoryService"/> over the queryable
/// <see cref="ModerationHistoryEntry"/> log.
/// </summary>
public sealed class ModerationHistoryService(IApplicationDbContext db, TimeProvider clock)
    : IModerationHistoryService
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

    public async Task<Result<ModerationHistoryEntryDto>> AddNoteAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        Guid moderatorUserId,
        string note,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(note))
            return Errors
                .ValidationFailed("A moderation note needs text.")
                .ToTyped<ModerationHistoryEntryDto>();

        User? subject = await db.Users.FirstOrDefaultAsync(u => u.Id == subjectUserId, ct);
        if (subject?.TwitchUserId is null)
            return Errors.NotFound<ModerationHistoryEntryDto>("User", subjectUserId.ToString());

        User? moderator = await db.Users.FirstOrDefaultAsync(u => u.Id == moderatorUserId, ct);

        ModerationHistoryEntry entry = new()
        {
            BroadcasterId = broadcasterId,
            SubjectUserId = subjectUserId,
            SubjectTwitchUserId = subject.TwitchUserId,
            ActionType = ModerationHistoryEntryKinds.Note,
            ModeratorUserId = moderatorUserId,
            ModeratorTwitchUserId = moderator?.TwitchUserId,
            ModeratorDisplayName = moderator?.DisplayName ?? moderator?.Username,
            Reason = note.Length > 500 ? note[..500] : note,
            OccurredAt = clock.GetUtcNow().UtcDateTime,
        };

        db.ModerationHistoryEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        return Result.Success(
            new ModerationHistoryEntryDto(
                entry.Id,
                entry.SubjectUserId,
                entry.SubjectTwitchUserId,
                entry.ActionType,
                entry.ModeratorUserId,
                entry.ModeratorDisplayName,
                entry.Reason,
                entry.DurationSeconds,
                entry.OccurredAt
            )
        );
    }
}
