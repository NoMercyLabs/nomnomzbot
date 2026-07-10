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
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Folds the journal into the per-channel daily aggregate (analytics.md §3.1, schema M.8 — no PII). Per-tenant
/// checkpoint; the runner applies each event once (checkpoint-gated) and a rebuild is <see cref="ResetAsync"/> then
/// replay, so the incrementing upsert is correct. Pure counts only — this row survives any viewer erasure.
/// </summary>
public sealed class ChannelAnalyticsDailyProjection(IApplicationDbContext db) : IProjection
{
    // "FollowEvent" is the live EventSub translation; "NewFollowerEvent" only exists in journals written by
    // legacy imports before the follow event was canonicalized — both must fold or a rebuild undercounts.
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
        "FollowEvent",
        "NewFollowerEvent",
        "NewSubscriptionEvent",
        "GiftSubscriptionEvent",
        "CheerEvent",
        "CommandExecutedEvent",
        "RewardRedeemedEvent",
        "SongRequestedEvent",
        "CurrencyCreditedEvent",
        "CurrencyDebitedEvent",
        "GamePlayedEvent",
    };

    public string Name => "analytics.channel-daily";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId is not Guid broadcasterId)
            return Result.Success(); // directory-level event — no channel to attribute it to

        DateOnly date = DateOnly.FromDateTime(@event.OccurredAt);
        ChannelAnalyticsDaily row = await GetOrCreateAsync(broadcasterId, date, cancellationToken);

        switch (@event.EventType)
        {
            case "ChatMessageReceivedEvent":
                row.TotalMessages++;
                break;
            case "FollowEvent":
            case "NewFollowerEvent":
                row.NewFollowers++;
                break;
            case "NewSubscriptionEvent":
            case "GiftSubscriptionEvent":
                row.NewSubscribers++;
                break;
            case "CommandExecutedEvent":
                // Only a run that actually did its work counts as "executed".
                if (ParseBool(@event.PayloadJson, "Succeeded"))
                    row.CommandsRun++;
                break;
            case "RewardRedeemedEvent":
                row.RedemptionsCount++;
                break;
            case "CheerEvent":
                row.BitsCheered += ParseAmount(@event.PayloadJson, "Bits");
                break;
            case "SongRequestedEvent":
                row.SongRequests++;
                break;
            case "CurrencyCreditedEvent":
                row.CurrencyEarnedTotal += ParseAmount(@event.PayloadJson, "Amount");
                break;
            case "CurrencyDebitedEvent":
                // A debit's Amount is the raw NEGATIVE ledger amount — fold its magnitude.
                row.CurrencySpentTotal += Math.Abs(ParseAmount(@event.PayloadJson, "Amount"));
                break;
            case "GamePlayedEvent":
                row.GamesPlayed++;
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
        List<ChannelAnalyticsDaily> rows = await (
            broadcasterId is Guid id
                ? db.ChannelAnalyticsDailies.Where(r => r.BroadcasterId == id)
                : db.ChannelAnalyticsDailies
        ).ToListAsync(cancellationToken);
        db.ChannelAnalyticsDailies.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<ChannelAnalyticsDaily> GetOrCreateAsync(
        Guid broadcasterId,
        DateOnly date,
        CancellationToken ct
    )
    {
        ChannelAnalyticsDaily? row = await db.ChannelAnalyticsDailies.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.ActivityDate == date,
            ct
        );
        if (row is null)
        {
            row = new ChannelAnalyticsDaily { BroadcasterId = broadcasterId, ActivityDate = date };
            db.ChannelAnalyticsDailies.Add(row);
        }
        return row;
    }

    private static long ParseAmount(string payloadJson, string field)
    {
        try
        {
            return JObject.Parse(payloadJson)[field]?.Value<long?>() ?? 0;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return 0;
        }
    }

    private static bool ParseBool(string payloadJson, string field)
    {
        try
        {
            return JObject.Parse(payloadJson)[field]?.Value<bool?>() ?? false;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return false;
        }
    }
}
