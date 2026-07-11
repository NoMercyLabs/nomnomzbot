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
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Chat.Entities;

namespace NomNomzBot.Infrastructure.Chat.YouTube;

/// <summary>
/// <see cref="IYouTubeLiveChatBanLedger"/> over the <see cref="YouTubeLiveChatBan"/> table. Consume
/// soft-deletes (never hard-deletes) so the moderation trail stays auditable; the global soft-delete filter
/// keeps consumed rows out of the next lookup.
/// </summary>
public sealed class YouTubeLiveChatBanLedger : IYouTubeLiveChatBanLedger
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public YouTubeLiveChatBanLedger(IApplicationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task RecordAsync(
        Guid broadcasterId,
        Guid primaryBroadcasterId,
        string liveChatId,
        string bannedChannelId,
        string banId,
        int? durationSeconds,
        CancellationToken cancellationToken = default
    )
    {
        await _db.YouTubeLiveChatBans.AddAsync(
            new YouTubeLiveChatBan
            {
                BroadcasterId = broadcasterId,
                PrimaryBroadcasterId = primaryBroadcasterId,
                LiveChatId = liveChatId,
                BannedChannelId = bannedChannelId,
                BanId = banId,
                BanType = durationSeconds is null ? "permanent" : "temporary",
                DurationSeconds = durationSeconds,
            },
            cancellationToken
        );
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<YouTubeConsumedBan?> ConsumeLatestAsync(
        Guid broadcasterId,
        string bannedChannelId,
        CancellationToken cancellationToken = default
    )
    {
        // The consumed-row exclusion is explicit (not left to the ambient soft-delete filter) so the
        // ledger's own contract holds in any context. Id is a UUIDv7 (time-ordered) — the tie-break keeps
        // "newest" deterministic when two bans land inside one CreatedAt tick (timeout immediately
        // escalated to a permanent ban).
        YouTubeLiveChatBan? latest = await _db
            .YouTubeLiveChatBans.Where(b =>
                b.BroadcasterId == broadcasterId
                && b.BannedChannelId == bannedChannelId
                && b.DeletedAt == null
            )
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
            return null;

        latest.DeletedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);
        return new YouTubeConsumedBan(latest.BanId, latest.PrimaryBroadcasterId);
    }
}
