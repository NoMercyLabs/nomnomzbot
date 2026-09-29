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
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.EventStore;

namespace NomNomzBot.Infrastructure.Dashboard.Replay;

/// <summary>
/// Finds the named recipients of one gift bomb, in the order they were announced. Twitch's
/// <c>channel.subscription.gift</c> names no recipients, so the link comes from the recipients' own
/// <see cref="GiftSubscriptionReceivedEvent"/>s (the <c>sub_gift</c> chat notices), matched on two keys:
/// <list type="bullet">
/// <item>the GIFTER — a notice carries the gifter's id. The gifter key is required: when a second gifter
/// tops up a bomb ("We added 1 Gift Subs to QTkittE's gift!"), Twitch puts that extra sub under the SAME
/// community gift id, so the id alone would pull the other gifter's recipient into this chain;</item>
/// <item>the COMMUNITY GIFT ID — it separates two bombs by the same gifter close together. When several
/// batches match, the one announced nearest the bomb wins.</item>
/// </list>
/// The search is bounded to a short window around the bomb, and the chain is capped at its gift count.
/// </summary>
public sealed class GiftBombChainResolver
{
    // Twitch delivers the sub_gift notices within seconds of the bomb; the margin covers a slow EventSub socket.
    private static readonly TimeSpan SearchWindow = TimeSpan.FromMinutes(2);

    private readonly IApplicationDbContext _db;
    private readonly JournaledDomainEventReader _reader;

    public GiftBombChainResolver(IApplicationDbContext db, JournaledDomainEventReader reader)
    {
        _db = db;
        _reader = reader;
    }

    public async Task<List<ActivityReplayItem>> RecipientsOfAsync(
        GiftSubscriptionEvent bomb,
        DateTime bombOccurredAt,
        CancellationToken ct
    )
    {
        List<(DateTime OccurredAt, GiftSubscriptionReceivedEvent Gift)> sameGifter =
            await SameGifterRecipientsAsync(bomb, bombOccurredAt, ct);
        if (sameGifter.Count == 0)
            return [];

        // Pick the batch whose first notice landed nearest the bomb, then keep its announcement order.
        IGrouping<string, (DateTime OccurredAt, GiftSubscriptionReceivedEvent Gift)> batch =
            sameGifter
                .GroupBy(r => r.Gift.CommunityGiftId ?? string.Empty)
                .MinBy(g => (g.First().OccurredAt - bombOccurredAt).Duration())!;

        return
        [
            .. batch
                .Take(bomb.GiftCount)
                .Select(r => new ActivityReplayItem(r.Gift.EventId.ToString(), r.Gift)),
        ];
    }

    private async Task<
        List<(DateTime OccurredAt, GiftSubscriptionReceivedEvent Gift)>
    > SameGifterRecipientsAsync(
        GiftSubscriptionEvent bomb,
        DateTime bombOccurredAt,
        CancellationToken ct
    )
    {
        DateTime from = bombOccurredAt - SearchWindow;
        DateTime to = bombOccurredAt + SearchWindow;
        List<EventJournal> rows = await _db
            .EventJournals.AsNoTracking()
            .Where(e =>
                e.BroadcasterId == bomb.BroadcasterId
                && e.EventType == nameof(GiftSubscriptionReceivedEvent)
                && e.OccurredAt >= from
                && e.OccurredAt <= to
            )
            .OrderBy(e => e.StreamPosition)
            .ToListAsync(ct);

        List<(DateTime OccurredAt, GiftSubscriptionReceivedEvent Gift)> matches = [];
        foreach (EventJournal row in rows)
        {
            Result<IDomainEvent> read = await _reader.ReadAsync(row, ct);
            if (
                read is { IsSuccess: true, Value: GiftSubscriptionReceivedEvent gift }
                && IsFromSameGifter(bomb, gift)
                && gift.Tier == bomb.Tier
            )
                matches.Add((row.OccurredAt, gift));
        }

        return matches;
    }

    private static bool IsFromSameGifter(
        GiftSubscriptionEvent bomb,
        GiftSubscriptionReceivedEvent gift
    ) =>
        bomb.IsAnonymous
            ? gift.IsAnonymous
            : !gift.IsAnonymous
                && string.Equals(gift.GifterUserId, bomb.GifterUserId, StringComparison.Ordinal);
}
