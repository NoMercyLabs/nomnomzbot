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
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        if (@event.BroadcasterId is not { } broadcasterId)
            return Result.Success(); // directory-level event — no channel to attribute it to

        DateOnly date = DateOnly.FromDateTime(@event.OccurredAt);

        // Insert-if-missing, tolerating a concurrent writer (the driver's tick vs. a manual rebuild, or
        // two rebuild workers) winning the race to mint the SAME (BroadcasterId, ActivityDate) row —
        // that used to surface as an Npgsql 23505 duplicate-key violation. Whichever writer loses the
        // insert simply proceeds to the atomic increments below against the row the winner created.
        await EnsureDailyRowExistsAsync(broadcasterId, date, cancellationToken);

        long messagesDelta = 0;
        long followersDelta = 0;
        long subscribersDelta = 0;
        long commandsDelta = 0;
        long redemptionsDelta = 0;
        long bitsDelta = 0;
        long songRequestsDelta = 0;
        long currencyEarnedDelta = 0;
        long currencySpentDelta = 0;
        long gamesDelta = 0;
        long uniqueChattersDelta = 0;
        long watchSecondsDelta = 0;
        int? peakViewersCandidate = null;

        if (PresenceEvents.Contains(@event.EventType))
        {
            (long ChattersDelta, long SecondsDelta) presence = await FoldPresenceAsync(
                broadcasterId,
                date,
                @event,
                cancellationToken
            );
            uniqueChattersDelta = presence.ChattersDelta;
            watchSecondsDelta = presence.SecondsDelta;
        }

        switch (@event.EventType)
        {
            case "ChatMessageReceivedEvent":
                messagesDelta = 1;
                break;
            case "FollowEvent":
            case "NewFollowerEvent":
                followersDelta = 1;
                break;
            case "NewSubscriptionEvent":
            case "ResubscriptionEvent":
            case "GiftSubscriptionEvent":
                // All sub activity counts — new, resub (channel.subscription.message), and gift. A resub was
                // previously dropped entirely, so a channel with only renewals showed 0 despite active subs.
                subscribersDelta = 1;
                break;
            case "CommandExecutedEvent":
                // Only a run that actually did its work counts as "executed".
                if (ParseBool(@event.PayloadJson, "Succeeded"))
                    commandsDelta = 1;
                break;
            case "RewardRedeemedEvent":
                redemptionsDelta = 1;
                break;
            case "CheerEvent":
                bitsDelta = ParseAmount(@event.PayloadJson, "Bits");
                break;
            case "SongRequestedEvent":
                songRequestsDelta = 1;
                break;
            case "CurrencyCreditedEvent":
                currencyEarnedDelta = ParseAmount(@event.PayloadJson, "Amount");
                break;
            case "CurrencyDebitedEvent":
                // A debit's Amount is the raw NEGATIVE ledger amount — fold its magnitude.
                currencySpentDelta = Math.Abs(ParseAmount(@event.PayloadJson, "Amount"));
                break;
            case "GamePlayedEvent":
                gamesDelta = 1;
                break;
            case "StreamViewerCountSampledEvent":
                peakViewersCandidate = (int)ParseAmount(@event.PayloadJson, "ViewerCount");
                break;
        }

        // One atomic UPDATE covering every counter this event touches — each SetProperty is a DB-side
        // `column = column + delta` (delta is 0 for anything this event didn't touch), so two concurrent
        // folds of the SAME row never read-modify-write a stale in-memory copy and lose one side's count.
        int peakViewersLocal = peakViewersCandidate ?? 0;
        bool hasPeakCandidate = peakViewersCandidate is not null;
        await db
            .ChannelAnalyticsDailies.IgnoreQueryFilters()
            .Where(r => r.BroadcasterId == broadcasterId && r.ActivityDate == date)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(r => r.TotalMessages, r => r.TotalMessages + messagesDelta)
                        .SetProperty(r => r.NewFollowers, r => r.NewFollowers + followersDelta)
                        .SetProperty(
                            r => r.NewSubscribers,
                            r => r.NewSubscribers + subscribersDelta
                        )
                        .SetProperty(r => r.CommandsRun, r => r.CommandsRun + commandsDelta)
                        .SetProperty(
                            r => r.RedemptionsCount,
                            r => r.RedemptionsCount + redemptionsDelta
                        )
                        .SetProperty(r => r.BitsCheered, r => r.BitsCheered + bitsDelta)
                        .SetProperty(r => r.SongRequests, r => r.SongRequests + songRequestsDelta)
                        .SetProperty(
                            r => r.CurrencyEarnedTotal,
                            r => r.CurrencyEarnedTotal + currencyEarnedDelta
                        )
                        .SetProperty(
                            r => r.CurrencySpentTotal,
                            r => r.CurrencySpentTotal + currencySpentDelta
                        )
                        .SetProperty(r => r.GamesPlayed, r => r.GamesPlayed + gamesDelta)
                        .SetProperty(
                            r => r.UniqueChatters,
                            r => r.UniqueChatters + uniqueChattersDelta
                        )
                        .SetProperty(
                            r => r.TotalWatchSeconds,
                            r => r.TotalWatchSeconds + watchSecondsDelta
                        )
                        .SetProperty(
                            r => r.PeakViewers,
                            r =>
                                !hasPeakCandidate ? r.PeakViewers
                                : r.PeakViewers == null || peakViewersLocal > r.PeakViewers
                                    ? peakViewersLocal
                                : r.PeakViewers
                        ),
                cancellationToken
            );

        // ExecuteUpdateAsync bypasses the change tracker entirely. If ANY earlier tracking query in this
        // same unit of work already materialized this exact (BroadcasterId, ActivityDate) row, EF's identity
        // map would keep handing that never-updated instance back to every later query for it instead of
        // re-reading the value this call just wrote — detaching it forces the next read to be fresh.
        if (db is DbContext dbContextAfterUpdate)
        {
            foreach (
                EntityEntry<ChannelAnalyticsDaily> entry in dbContextAfterUpdate
                    .ChangeTracker.Entries<ChannelAnalyticsDaily>()
                    .Where(e =>
                        e.Entity.BroadcasterId == broadcasterId && e.Entity.ActivityDate == date
                    )
                    .ToList()
            )
                entry.State = EntityState.Detached;
        }

        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ChannelAnalyticsDaily> rows = await (
            broadcasterId is { } id
                ? db.ChannelAnalyticsDailies.Where(r => r.BroadcasterId == id)
                : db.ChannelAnalyticsDailies
        ).ToListAsync(cancellationToken);
        db.ChannelAnalyticsDailies.RemoveRange(rows);

        // The distinctness/presence anchor is owned by this projection — it resets with the aggregate,
        // or a replay would see every chatter as "already counted".
        List<ChannelChatterDay> anchors = await (
            broadcasterId is { } anchorTenant
                ? db.ChannelChatterDays.Where(r => r.BroadcasterId == anchorTenant)
                : db.ChannelChatterDays
        ).ToListAsync(cancellationToken);
        db.ChannelChatterDays.RemoveRange(anchors);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Folds one presence event into the (channel, day, viewer-hash) anchor and returns the (UniqueChatters,
    /// TotalWatchSeconds) deltas to fold into the daily row's own atomic update: first sight mints the anchor,
    /// a first CHAT flips the viewer into <c>UniqueChatters</c>, and consecutive presence inside the SAME live
    /// stream extends <c>TotalWatchSeconds</c> by the gap — per-stream first→last span, exactly the M.2
    /// watch-session semantics (never across streams or offline gaps). The anchor mutation itself is CAS-retried
    /// so two concurrent folds of the SAME viewer never lose either side's update.
    /// </summary>
    private async Task<(long ChattersDelta, long SecondsDelta)> FoldPresenceAsync(
        Guid broadcasterId,
        DateOnly date,
        EventRecord @event,
        CancellationToken ct
    )
    {
        (string Provider, string ExternalUserId, string Login, string Display)? identity =
            ViewerResolver.ParseIdentity(@event.PayloadJson);
        if (identity is null)
            return (0, 0);

        string hash = ChatterHash(identity.Value.Provider, identity.Value.ExternalUserId);
        bool isChat = @event.EventType == "ChatMessageReceivedEvent";
        string? streamId = await liveWindow.GetCoveringStreamIdAsync(
            broadcasterId,
            @event.OccurredAt,
            ct
        );

        if (
            await TryInsertAnchorAsync(
                broadcasterId,
                date,
                hash,
                isChat,
                @event.OccurredAt,
                streamId,
                ct
            )
        )
            return (isChat ? 1 : 0, 0);

        // The anchor already existed — either before this call, or a concurrent writer just won the insert
        // race above. CAS-retry the chat flip + last-seen advance against it.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            ChannelChatterDay? anchor = await db
                .ChannelChatterDays.IgnoreQueryFilters() // tenant-less projection-driver / rebuild scope
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    a =>
                        a.BroadcasterId == broadcasterId
                        && a.ActivityDate == date
                        && a.ChatterHash == hash,
                    ct
                );
            if (anchor is null)
            {
                // Vanishingly rare: a concurrent ResetAsync removed the row between our failed insert and
                // this read — try minting it again.
                if (
                    await TryInsertAnchorAsync(
                        broadcasterId,
                        date,
                        hash,
                        isChat,
                        @event.OccurredAt,
                        streamId,
                        ct
                    )
                )
                    return (isChat ? 1 : 0, 0);
                continue;
            }

            bool flipsChat = isChat && !anchor.Chatted;
            bool advances = @event.OccurredAt > anchor.LastSeenAt;
            long secondsDelta =
                advances && streamId is not null && anchor.LastStreamId == streamId
                    ? (long)(@event.OccurredAt - anchor.LastSeenAt).TotalSeconds
                    : 0;

            if (!flipsChat && !advances)
                return (0, 0); // a stale/duplicate replay of an already-folded moment — nothing to change

            DateTime expectedLastSeen = anchor.LastSeenAt;
            bool expectedChatted = anchor.Chatted;
            int updated = await db
                .ChannelChatterDays.IgnoreQueryFilters()
                .Where(a =>
                    a.Id == anchor.Id
                    && a.LastSeenAt == expectedLastSeen
                    && a.Chatted == expectedChatted
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(a => a.Chatted, a => a.Chatted || isChat)
                            .SetProperty(
                                a => a.LastSeenAt,
                                a => advances ? @event.OccurredAt : a.LastSeenAt
                            )
                            .SetProperty(
                                a => a.LastStreamId,
                                a => advances ? streamId : a.LastStreamId
                            ),
                    ct
                );
            if (updated > 0)
            {
                // Same identity-map hazard as the daily row's atomic update above — detach any tracked
                // instance of this anchor so a later query re-reads the CAS'd value rather than a stale copy.
                if (db is DbContext dbContextAfterAnchorUpdate)
                {
                    foreach (
                        EntityEntry<ChannelChatterDay> entry in dbContextAfterAnchorUpdate
                            .ChangeTracker.Entries<ChannelChatterDay>()
                            .Where(e => e.Entity.Id == anchor.Id)
                            .ToList()
                    )
                        entry.State = EntityState.Detached;
                }
                return (flipsChat ? 1 : 0, secondsDelta);
            }
            // Someone else moved the anchor between our read and our CAS — retry with a fresh read.
        }

        // Exhausted retries under heavy contention — skip this fold rather than corrupt the anchor; the
        // next presence event for this viewer will pick the span back up.
        return (0, 0);
    }

    /// <summary>The shared <see cref="ChatterIdentityHash"/> — one hash, every consumer (giveaway
    /// watch-time eligibility looks these rows back up with it).</summary>
    private static string ChatterHash(string provider, string externalUserId) =>
        ChatterIdentityHash.Compute(provider, externalUserId);

    /// <summary>
    /// Inserts a fresh (channel, day, viewer-hash) anchor row, tolerating a concurrent writer minting the
    /// same key first. This collision is EXPECTED and BY DESIGN under concurrent first-message-of-the-day
    /// folds — the caller's CAS retry loop in <c>FoldPresenceAsync</c> absorbs it — so it must never surface
    /// as an error-level log. A tracked <c>Add</c> + <c>SaveChangesAsync</c> that lets the unique-index
    /// violation reach the database raises a real <see cref="DbUpdateException"/>, and EF's relational
    /// command diagnostics log the failing command at Error BEFORE that exception is thrown — catching it
    /// here does not stop the log. Instead this issues a conflict-tolerant raw insert (native "ON CONFLICT
    /// DO NOTHING" / "INSERT OR IGNORE") so the database never reports a constraint failure at all: no
    /// exception, no error log, on either provider. A genuine save failure elsewhere in this projection
    /// still goes through the normal tracked <c>SaveChangesAsync</c> path and is still logged at Error —
    /// only this one known-benign race is silenced.
    /// </summary>
    private async Task<bool> TryInsertAnchorAsync(
        Guid broadcasterId,
        DateOnly date,
        string hash,
        bool isChat,
        DateTime occurredAt,
        string? streamId,
        CancellationToken ct
    )
    {
        if (db is not DbContext dbContext)
        {
            // No real DbContext to issue provider-native raw SQL against (e.g. a hand-written test fake) —
            // fall back to the tracked insert; the caller's retry loop still tolerates the collision, it
            // simply keeps the pre-existing error-level log in that fallback case.
            return await TryInsertAnchorViaTrackedSaveAsync(
                broadcasterId,
                date,
                hash,
                isChat,
                occurredAt,
                streamId,
                ct
            );
        }

        int affected = dbContext.Database.IsNpgsql()
            ? await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "ChannelChatterDays"
                    ("BroadcasterId", "ActivityDate", "ChatterHash", "Chatted", "FirstSeenAt", "LastSeenAt", "LastStreamId")
                VALUES
                    ({broadcasterId}, {date}, {hash}, {isChat}, {occurredAt}, {occurredAt}, {streamId})
                ON CONFLICT ("BroadcasterId", "ActivityDate", "ChatterHash") DO NOTHING
                """,
                ct
            )
            : await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT OR IGNORE INTO "ChannelChatterDays"
                    ("BroadcasterId", "ActivityDate", "ChatterHash", "Chatted", "FirstSeenAt", "LastSeenAt", "LastStreamId")
                VALUES
                    ({broadcasterId}, {date}, {hash}, {isChat}, {occurredAt}, {occurredAt}, {streamId})
                """,
                ct
            );

        return affected > 0;
    }

    /// <summary>
    /// Pre-existing tracked-insert fallback (used only when <c>db</c> isn't a real <see cref="DbContext"/>):
    /// still correct, but a losing race here surfaces the collision as an error-level EF log — see the
    /// caller's remarks.
    /// </summary>
    private async Task<bool> TryInsertAnchorViaTrackedSaveAsync(
        Guid broadcasterId,
        DateOnly date,
        string hash,
        bool isChat,
        DateTime occurredAt,
        string? streamId,
        CancellationToken ct
    )
    {
        ChannelChatterDay anchor = new()
        {
            BroadcasterId = broadcasterId,
            ActivityDate = date,
            ChatterHash = hash,
            Chatted = isChat,
            FirstSeenAt = occurredAt,
            LastSeenAt = occurredAt,
            LastStreamId = streamId,
        };
        db.ChannelChatterDays.Add(anchor);
        try
        {
            await db.SaveChangesAsync(ct);
            // Detach even on success: every later mutation of this row goes through ExecuteUpdateAsync,
            // which bypasses the change tracker — an entity left tracked here would make EF's identity map
            // hand back this never-updated in-memory instance to any later query for the same row instead
            // of its current DB values.
            if (db is DbContext dbContextOnInsert)
                dbContextOnInsert.Entry(anchor).State = EntityState.Detached;
            return true;
        }
        catch (DbUpdateException)
        {
            if (db is DbContext dbContext)
                dbContext.Entry(anchor).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>
    /// Inserts a fresh daily row, tolerating a concurrent writer (the driver's tick vs. a manual rebuild)
    /// minting the same (BroadcasterId, ActivityDate) row first — the unique index then rejects ours, which
    /// used to surface as an Npgsql 23505 duplicate-key violation instead of being absorbed here. Same
    /// conflict-tolerant-raw-insert mechanism as <see cref="TryInsertAnchorAsync"/>: a tracked <c>Add</c> +
    /// <c>SaveChangesAsync</c> that lets the unique-index violation reach the database has EF's relational
    /// command diagnostics log the failing command at Error BEFORE the exception is thrown — catching it
    /// does not stop the log. The provider-native "ON CONFLICT DO NOTHING" / "INSERT OR IGNORE" insert never
    /// reports a constraint failure at all, so this expected, benign race never surfaces as an error log.
    /// </summary>
    private async Task EnsureDailyRowExistsAsync(
        Guid broadcasterId,
        DateOnly date,
        CancellationToken ct
    )
    {
        bool exists = await db
            .ChannelAnalyticsDailies.IgnoreQueryFilters()
            .AnyAsync(r => r.BroadcasterId == broadcasterId && r.ActivityDate == date, ct);
        if (exists)
            return;

        if (db is not DbContext dbContext)
        {
            // No real DbContext to issue provider-native raw SQL against (e.g. a hand-written test fake) —
            // fall back to the tracked insert; a losing race here still lands correctly, it simply keeps
            // the pre-existing error-level log in that fallback case.
            await EnsureDailyRowExistsViaTrackedSaveAsync(broadcasterId, date, ct);
            return;
        }

        // Every NOT NULL counter column has no SQL-level DEFAULT, so it must be listed explicitly — SQLite's
        // "INSERT OR IGNORE" silently ignores ANY constraint failure (not just the unique-index collision
        // this is meant to tolerate), so an omitted NOT NULL column would make the insert a silent no-op
        // instead of minting the row. The values mirror the entity's own CLR defaults (zero counters, no
        // peak-viewers sample yet).
        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "ChannelAnalyticsDailies"
                    ("BroadcasterId", "ActivityDate", "UniqueChatters", "TotalMessages", "TotalWatchSeconds",
                     "NewFollowers", "NewSubscribers", "BitsCheered", "CommandsRun", "RedemptionsCount",
                     "SongRequests", "CurrencyEarnedTotal", "CurrencySpentTotal", "GamesPlayed", "PeakViewers")
                VALUES
                    ({broadcasterId}, {date}, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, NULL)
                ON CONFLICT ("BroadcasterId", "ActivityDate") DO NOTHING
                """,
                ct
            );
        else
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT OR IGNORE INTO "ChannelAnalyticsDailies"
                    ("BroadcasterId", "ActivityDate", "UniqueChatters", "TotalMessages", "TotalWatchSeconds",
                     "NewFollowers", "NewSubscribers", "BitsCheered", "CommandsRun", "RedemptionsCount",
                     "SongRequests", "CurrencyEarnedTotal", "CurrencySpentTotal", "GamesPlayed", "PeakViewers")
                VALUES
                    ({broadcasterId}, {date}, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, NULL)
                """,
                ct
            );
    }

    /// <summary>
    /// Pre-existing tracked-insert fallback (used only when <c>db</c> isn't a real <see cref="DbContext"/>):
    /// still correct, but a losing race here surfaces the collision as an error-level EF log — see the
    /// caller's remarks.
    /// </summary>
    private async Task EnsureDailyRowExistsViaTrackedSaveAsync(
        Guid broadcasterId,
        DateOnly date,
        CancellationToken ct
    )
    {
        ChannelAnalyticsDaily row = new() { BroadcasterId = broadcasterId, ActivityDate = date };
        db.ChannelAnalyticsDailies.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            // Detach even on success: every counter mutation below goes through ExecuteUpdateAsync, which
            // bypasses the change tracker — an entity left tracked here would make EF's identity map hand
            // back this never-updated in-memory instance to any later query for the same row.
            if (db is DbContext dbContextOnInsert)
                dbContextOnInsert.Entry(row).State = EntityState.Detached;
        }
        catch (DbUpdateException)
        {
            if (db is DbContext dbContext)
                dbContext.Entry(row).State = EntityState.Detached;
        }
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
