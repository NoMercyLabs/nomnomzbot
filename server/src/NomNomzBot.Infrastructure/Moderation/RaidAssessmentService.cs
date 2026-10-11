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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Raids;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Vets the raider of every incoming raid. The raid journal may already hold the current raid when this
/// runs, so a latest raid within <see cref="JournalRaceWindow"/> of the event is not counted as earlier.
/// </summary>
public sealed class RaidAssessmentService(
    IApplicationDbContext db,
    ITwitchUsersApi users,
    IRaidHistoryReader history,
    IEventBus bus
) : IRaidAssessmentService
{
    private const string ConfigDomain = "raid-assessments";
    private static readonly TimeSpan JournalRaceWindow = TimeSpan.FromSeconds(30);

    public async Task<Result<RaidAssessmentDto>> AssessAsync(
        RaidEvent raid,
        CancellationToken ct = default
    )
    {
        string? ownTwitchId = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == raid.BroadcasterId)
            .Select(c => c.ExternalChannelId)
            .FirstOrDefaultAsync(ct);
        if (ownTwitchId is null)
            return Result.Failure<RaidAssessmentDto>("Channel not found.", "CHANNEL_NOT_FOUND");

        bool isBanned = await IsBannedAsync(raid, ct);
        bool isNetworkBlocked = await db.NetworkBlocks.AnyAsync(
            b =>
                b.TargetTwitchUserId == raid.FromUserId
                && (
                    b.Status == NetworkBlockStatus.Active || b.Status == NetworkBlockStatus.Partial
                ),
            ct
        );

        RaidAssessment row = new()
        {
            BroadcasterId = raid.BroadcasterId,
            RaiderTwitchUserId = raid.FromUserId,
            RaiderLogin = raid.FromLogin,
            RaiderDisplayName = raid.FromDisplayName,
            ViewerCount = raid.ViewerCount,
            AccountCreatedAt = await LookupAccountAgeAsync(raid.FromUserId, ct),
            EarlierRaidCount = await CountEarlierRaidsAsync(raid, ownTwitchId, ct),
            IsBanned = isBanned,
            IsNetworkBlocked = isNetworkBlocked,
            OccurredAt = raid.OccurredAt.UtcDateTime,
        };
        row.Verdict = DecideVerdict(row);

        db.RaidAssessments.Add(row);
        await db.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = raid.BroadcasterId,
                Domain = ConfigDomain,
                EntityId = row.Id.ToString(),
                Action = "created",
            },
            ct
        );

        return Result.Success(ToDto(row));
    }

    public async Task<Result<PagedList<RaidAssessmentDto>>> ListRecentAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<RaidAssessment> channelRows = db
            .RaidAssessments.AsNoTracking()
            .Where(a => a.BroadcasterId == broadcasterId);

        int total = await channelRows.CountAsync(ct);
        List<RaidAssessment> page = await channelRows
            .OrderByDescending(a => a.OccurredAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<RaidAssessmentDto>(
                page.Select(ToDto).ToList(),
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    private static string DecideVerdict(RaidAssessment row)
    {
        if (row.IsBanned || row.IsNetworkBlocked)
            return RaidVerdicts.Hostile;
        return row.EarlierRaidCount >= 1 ? RaidVerdicts.Known : RaidVerdicts.Unknown;
    }

    private async Task<bool> IsBannedAsync(RaidEvent raid, CancellationToken ct)
    {
        string? latest = await db
            .ModerationHistoryEntries.AsNoTracking()
            .Where(e =>
                e.BroadcasterId == raid.BroadcasterId
                && e.SubjectTwitchUserId == raid.FromUserId
                && (
                    e.ActionType == ModerationHistoryEntryKinds.Ban
                    || e.ActionType == ModerationHistoryEntryKinds.Unban
                )
            )
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => e.ActionType)
            .FirstOrDefaultAsync(ct);
        return latest == ModerationHistoryEntryKinds.Ban;
    }

    private async Task<DateTime?> LookupAccountAgeAsync(string twitchUserId, CancellationToken ct)
    {
        Result<IReadOnlyList<TwitchUser>> found = await users.GetUsersByIdsAsync(
            [twitchUserId],
            ct
        );
        if (found.IsFailure)
            return null;
        TwitchUser? user = found.Value.FirstOrDefault(u => u.Id == twitchUserId);
        return user?.CreatedAt.UtcDateTime;
    }

    private async Task<int> CountEarlierRaidsAsync(
        RaidEvent raid,
        string ownTwitchId,
        CancellationToken ct
    )
    {
        Result<RaidHistory> raids = await history.GetHistoryAsync(
            raid.BroadcasterId,
            ownTwitchId,
            ct
        );
        if (
            raids.IsFailure
            || !raids.Value.Incoming.TryGetValue(raid.FromUserId, out RaidStats? stats)
        )
            return 0;

        bool currentRaidAlreadyJournaled = stats.LastAt >= raid.OccurredAt - JournalRaceWindow;
        return Math.Max(0, stats.Count - (currentRaidAlreadyJournaled ? 1 : 0));
    }

    private static RaidAssessmentDto ToDto(RaidAssessment a) =>
        new(
            a.Id,
            a.RaiderTwitchUserId,
            a.RaiderLogin,
            a.RaiderDisplayName,
            a.ViewerCount,
            a.AccountCreatedAt,
            a.EarlierRaidCount,
            a.IsBanned,
            a.IsNetworkBlocked,
            a.Verdict,
            a.OccurredAt
        );
}
