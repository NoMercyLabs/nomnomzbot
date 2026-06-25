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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Rewards;

/// <summary>
/// Folds the journal into the channel-points redemption queue (<see cref="Redemption"/>, rewards.md): a
/// <c>RewardRedeemedEvent</c> upserts the redemption as <c>unfulfilled</c>; a <c>RewardRedemptionUpdatedEvent</c>
/// moves it to the new status (<c>fulfilled</c> / <c>canceled</c>). Both events carry the real Twitch redemption
/// id, so the queue entry and its later status change land on the SAME row. This is the read model behind the
/// Rewards page's queue + fulfil/refund actions. A reset hard-clears the channel's rows (pure read model).
/// </summary>
public sealed class RewardRedemptionProjection(IApplicationDbContext db) : IProjection
{
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "RewardRedeemedEvent",
        "RewardRedemptionUpdatedEvent",
    };

    public string Name => "reward-redemption";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId is not Guid broadcasterId)
            return Result.Success();

        JObject? payload = TryParse(@event.PayloadJson);
        string? redemptionId = payload?["RedemptionId"]?.Value<string>();
        if (string.IsNullOrEmpty(redemptionId))
            return Result.Success();

        Redemption? row = await db.Redemptions.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.RedemptionId == redemptionId,
            cancellationToken
        );
        if (row is null)
        {
            // The status update can arrive before the add was journaled (or after a partial import) — create the
            // row either way so a fulfilled/canceled redemption is never lost just because its add is missing.
            row = new Redemption
            {
                BroadcasterId = broadcasterId,
                RedemptionId = redemptionId,
                Status = "unfulfilled",
                RedeemedAt = @event.OccurredAt,
            };
            await db.Redemptions.AddAsync(row, cancellationToken);
        }

        // The identity fields ride BOTH events — keep them current.
        row.RewardId = payload!["RewardId"]?.Value<string>() ?? row.RewardId ?? string.Empty;
        row.RewardTitle =
            payload["RewardTitle"]?.Value<string>() ?? row.RewardTitle ?? string.Empty;
        row.UserId = payload["UserId"]?.Value<string>() ?? row.UserId ?? string.Empty;
        row.UserDisplayName =
            payload["UserDisplayName"]?.Value<string>() ?? row.UserDisplayName ?? string.Empty;
        row.UpdatedAt = @event.OccurredAt;

        switch (@event.EventType)
        {
            case "RewardRedeemedEvent":
                // The queued redemption. Cost + UserInput ride only the add; the redeem time is authoritative here.
                row.Cost = payload["Cost"]?.Value<int?>() ?? row.Cost;
                row.UserInput = payload["UserInput"]?.Value<string>() ?? row.UserInput;
                row.RedeemedAt = @event.OccurredAt;
                break;
            case "RewardRedemptionUpdatedEvent":
                // The status transition — the whole point of the queue (fulfilled/canceled leaves the pending view).
                row.Status = payload["Status"]?.Value<string>() ?? row.Status;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Redemption> rows = await (
            broadcasterId is Guid id
                ? db.Redemptions.Where(r => r.BroadcasterId == id)
                : db.Redemptions
        ).ToListAsync(cancellationToken);

        // Pure read model — a rebuild hard-clears the channel's rows; the replay re-folds them from the journal.
        db.Redemptions.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static JObject? TryParse(string json)
    {
        try
        {
            return JObject.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
