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
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using Record = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Dashboard.Stats;

/// <summary>
/// The people banned in one channel when the recap is built. Platform ids (the chat, redemption, song request and
/// channel-fact rows carry these) and local user ids (the command usage rows carry these) are both kept, so each
/// metric can ask in the key its own rows use.
/// </summary>
public sealed record BannedPeople(HashSet<string> PlatformIds, HashSet<Guid> UserIds)
{
    public static readonly BannedPeople None = new([], []);

    public bool HasPlatformId(string? platformId) =>
        platformId is not null && PlatformIds.Contains(platformId);
}

/// <summary>
/// The one place the recap decides who is banned. A person is banned when the newest ban or unban in the moderation
/// ledger is a ban, or when the channel's ban list holds them and the ledger has no unban for them. A timeout is
/// not a ban, and a person who was unbanned counts again. Read once per request and shared by every metric.
/// </summary>
public sealed class StreamBanFilter(IApplicationDbContext db)
{
    private Guid? _channelId;
    private BannedPeople? _banned;

    public async Task<BannedPeople> ForChannelAsync(Guid channelId, CancellationToken ct)
    {
        if (_channelId == channelId && _banned is not null)
            return _banned;

        _banned = await LoadAsync(channelId, ct);
        _channelId = channelId;
        return _banned;
    }

    private async Task<BannedPeople> LoadAsync(Guid channelId, CancellationToken ct)
    {
        List<string> banKinds =
        [
            ModerationHistoryEntryKinds.Ban,
            ModerationHistoryEntryKinds.Unban,
        ];
        var ledger = await db
            .ModerationHistoryEntries.AsNoTracking()
            .Where(e => e.BroadcasterId == channelId && banKinds.Contains(e.ActionType))
            .Select(e => new
            {
                e.SubjectTwitchUserId,
                e.SubjectUserId,
                e.ActionType,
                e.OccurredAt,
            })
            .ToListAsync(ct);

        Dictionary<string, bool> newestIsBan = ledger
            .GroupBy(e => e.SubjectTwitchUserId)
            .ToDictionary(
                g => g.Key,
                g =>
                    g.OrderByDescending(e => e.OccurredAt).First().ActionType
                    == ModerationHistoryEntryKinds.Ban
            );

        List<string> listed = await db
            .Configurations.AsNoTracking()
            .Where(c => c.BroadcasterId == channelId && c.Key.StartsWith("ban:"))
            .Select(c => c.Key.Substring(4))
            .ToListAsync(ct);

        HashSet<string> platformIds = newestIsBan
            .Where(p => p.Value)
            .Select(p => p.Key)
            .ToHashSet();
        foreach (string id in listed.Where(id => !newestIsBan.ContainsKey(id)))
            platformIds.Add(id);

        if (platformIds.Count == 0)
            return BannedPeople.None;

        List<string> ids = platformIds.ToList();
        List<Guid> known = await db
            .Users.AsNoTracking()
            .Where(u => u.TwitchUserId != null && ids.Contains(u.TwitchUserId))
            .Select(u => u.Id)
            .ToListAsync(ct);
        HashSet<Guid> userIds = known.ToHashSet();
        foreach (var entry in ledger.Where(e => platformIds.Contains(e.SubjectTwitchUserId)))
            userIds.Add(entry.SubjectUserId);

        return new BannedPeople(platformIds, userIds);
    }
}

/// <summary>Leaves banned people out of the rows a recap metric counts.</summary>
public static class StreamBanQueries
{
    public static IQueryable<ChatMessage> WithoutBanned(
        this IQueryable<ChatMessage> rows,
        BannedPeople banned
    )
    {
        List<string> ids = banned.PlatformIds.ToList();
        return rows.Where(m => !ids.Contains(m.UserId));
    }

    public static IQueryable<Redemption> WithoutBanned(
        this IQueryable<Redemption> rows,
        BannedPeople banned
    )
    {
        List<string> ids = banned.PlatformIds.ToList();
        return rows.Where(r => !ids.Contains(r.UserId));
    }

    public static IQueryable<Record> WithoutBanned(
        this IQueryable<Record> rows,
        BannedPeople banned
    )
    {
        List<string> ids = banned.PlatformIds.ToList();
        return rows.Where(r => !ids.Contains(r.UserId));
    }

    public static IQueryable<CommandUsage> WithoutBanned(
        this IQueryable<CommandUsage> rows,
        BannedPeople banned
    )
    {
        List<Guid> ids = banned.UserIds.ToList();
        return rows.Where(u => !ids.Contains(u.ViewerUserId));
    }
}
