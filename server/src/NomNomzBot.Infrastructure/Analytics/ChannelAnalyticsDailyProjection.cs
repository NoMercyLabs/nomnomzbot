// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Folds the journal into the per-channel daily aggregate (analytics.md §3.1, schema M.8 — no PII). Per-tenant
/// checkpoint; the runner applies each event once (checkpoint-gated) and a rebuild is <see cref="ResetAsync"/> then
/// replay, so the incrementing upsert is correct. Pure counts only — this row survives any viewer erasure.
/// Distinctness and presence (UniqueChatters / TotalWatchSeconds) fold through the projection-owned
/// <see cref="ChannelChatterDay"/> anchor (hashed viewer key, reset together): a viewer's first chat of the day
/// counts them once, and each presence event (chat/command/redemption) inside a live window extends their
/// first→last span — the same semantics as the M.2 watch sessions. PeakViewers folds the daily maximum of the
/// journaled Get Streams viewer-count samples.
/// </summary>
public sealed class ChannelAnalyticsDailyProjection(
    IApplicationDbContext db,
    ILiveWindowResolver liveWindow
) : IProjection
{
    // "FollowEvent" is the live EventSub translation; "NewFollowerEvent" only exists in journals written by
    // legacy imports before the follow event was canonicalized — both must fold or a rebuild undercounts.
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
        "FollowEvent",
        "NewFollowerEvent",
        "NewSubscriptionEvent",
        "ResubscriptionEvent",
        "GiftSubscriptionEvent",
        "CheerEvent",
        "CommandExecutedEvent",
        "RewardRedeemedEvent",
        "SongRequestedEvent",
        "CurrencyCreditedEvent",
        "CurrencyDebitedEvent",
        "GamePlayedEvent",
        "StreamViewerCountSampledEvent",
    };

    // The presence events whose first→last daily span feeds TotalWatchSeconds (mirrors WatchSessionProjection).
    private static readonly HashSet<string> PresenceEvents = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
        "CommandExecutedEvent",
        "RewardRedeemedEvent",
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

        if (PresenceEvents.Contains(@event.EventType))
            await FoldPresenceAsync(row, @event, cancellationToken);

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
            case "ResubscriptionEvent":
            case "GiftSubscriptionEvent":
                // All sub activity counts — new, resub (channel.subscription.message), and gift. A resub was
                // previously dropped entirely, so a channel with only renewals showed 0 despite active subs.
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
            case "StreamViewerCountSampledEvent":
                int viewers = (int)ParseAmount(@event.PayloadJson, "ViewerCount");
                if (row.PeakViewers is null || viewers > row.PeakViewers)
                    row.PeakViewers = viewers;
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

        // The distinctness/presence anchor is owned by this projection — it resets with the aggregate,
        // or a replay would see every chatter as "already counted".
        List<ChannelChatterDay> anchors = await (
            broadcasterId is Guid anchorTenant
                ? db.ChannelChatterDays.Where(r => r.BroadcasterId == anchorTenant)
                : db.ChannelChatterDays
        ).ToListAsync(cancellationToken);
        db.ChannelChatterDays.RemoveRange(anchors);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Folds one presence event into the (channel, day, viewer-hash) anchor: first sight mints the row,
    /// a first CHAT flips the viewer into <c>UniqueChatters</c>, and consecutive presence inside the SAME
    /// live stream extends <c>TotalWatchSeconds</c> by the gap — per-stream first→last span, exactly the
    /// M.2 watch-session semantics (never across streams or offline gaps).
    /// </summary>
    private async Task FoldPresenceAsync(
        ChannelAnalyticsDaily row,
        EventRecord @event,
        CancellationToken ct
    )
    {
        (string Provider, string ExternalUserId, string Login, string Display)? identity =
            ViewerResolver.ParseIdentity(@event.PayloadJson);
        if (identity is null)
            return;

        string hash = ChatterHash(identity.Value.Provider, identity.Value.ExternalUserId);
        bool isChat = @event.EventType == "ChatMessageReceivedEvent";
        string? streamId = await liveWindow.GetCoveringStreamIdAsync(
            row.BroadcasterId,
            @event.OccurredAt,
            ct
        );

        ChannelChatterDay? anchor = await db.ChannelChatterDays.FirstOrDefaultAsync(
            a =>
                a.BroadcasterId == row.BroadcasterId
                && a.ActivityDate == row.ActivityDate
                && a.ChatterHash == hash,
            ct
        );

        if (anchor is null)
        {
            db.ChannelChatterDays.Add(
                new ChannelChatterDay
                {
                    BroadcasterId = row.BroadcasterId,
                    ActivityDate = row.ActivityDate,
                    ChatterHash = hash,
                    Chatted = isChat,
                    FirstSeenAt = @event.OccurredAt,
                    LastSeenAt = @event.OccurredAt,
                    LastStreamId = streamId,
                }
            );
            if (isChat)
                row.UniqueChatters++;
            return;
        }

        if (isChat && !anchor.Chatted)
        {
            anchor.Chatted = true;
            row.UniqueChatters++;
        }

        if (@event.OccurredAt > anchor.LastSeenAt)
        {
            if (streamId is not null && anchor.LastStreamId == streamId)
                row.TotalWatchSeconds += (long)(@event.OccurredAt - anchor.LastSeenAt).TotalSeconds;
            anchor.LastSeenAt = @event.OccurredAt;
            anchor.LastStreamId = streamId;
        }
    }

    /// <summary>The shared <see cref="ChatterIdentityHash"/> — one hash, every consumer (giveaway
    /// watch-time eligibility looks these rows back up with it).</summary>
    private static string ChatterHash(string provider, string externalUserId) =>
        ChatterIdentityHash.Compute(provider, externalUserId);

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
