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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Folds the journal's channel-fact events into the dashboard channel-event-log read model (event-store §3.3, schema
/// F.4 <c>TwitchChannelEventLog</c>, surfaced through the live <see cref="ChannelEvent"/> table). One row per journaled
/// fact — follow, sub, resub, gift, cheer, raid, reward redemption, ban/timeout, sub-end, moderator add/remove, and a
/// chat message — tenant-scoped by <c>BroadcasterId</c>. The row key is the journal <c>EventId</c> (string), so the
/// fold is idempotent: replay or a re-applied event upserts the same row, never a duplicate.
/// <para>
/// The fold snapshots the actor's Twitch id + display name into the scrubbed <c>Data</c> JSON (so the row renders
/// without a join) and writes a stable <c>actorTwitchUserId</c> key spanning every event type (chat/follow/sub credit
/// the chatter, a raid the raider, a gift the gifter, a ban the banned user). The internal <c>UserId</c> FK is left
/// null here and linked AFTER the rebuild by <see cref="ChannelEventActorBackfill"/> in ONE set-based pass
/// (bulk get-or-create Users, then a single UPDATE) — orders of magnitude cheaper than a per-event lookup, and the
/// rebuild stays a pure, external-call-free journal replay.
/// </para>
/// Reset → replay rebuilds the whole log from <c>EventJournal</c> alone, with no live Twitch call.
/// </summary>
public sealed class TwitchChannelEventLogProjection(IApplicationDbContext db) : IProjection
{
    // The journal EventType names this log surfaces, each mapped to a stable channel.* dashboard type. These are the
    // count-bearing channel facts a viewer sees in the activity feed — both the live EventSub-sourced events and the
    // legacy-import-sourced ones (the importer rebuilds these same domain events, so one fold serves both).
    private static readonly IReadOnlyDictionary<string, string> EventTypeToChannelType =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ChatMessageReceivedEvent"] = "channel.chat.message",
            ["NewFollowerEvent"] = "channel.follow",
            ["NewSubscriptionEvent"] = "channel.subscribe",
            ["ResubscriptionEvent"] = "channel.subscription.message",
            ["GiftSubscriptionEvent"] = "channel.subscription.gift",
            ["SubscriptionEndedEvent"] = "channel.subscription.end",
            ["CheerEvent"] = "channel.cheer",
            ["RaidEvent"] = "channel.raid",
            ["RewardRedeemedEvent"] = "channel.channel_points_custom_reward_redemption.add",
            ["UserBannedEvent"] = "channel.ban",
            ["UserTimedOutEvent"] = "channel.timeout",
            ["ModeratorAddedEvent"] = "channel.moderator.add",
            ["ModeratorRemovedEvent"] = "channel.moderator.remove",
        };

    private static readonly HashSet<string> Subscribed = new(
        EventTypeToChannelType.Keys,
        StringComparer.Ordinal
    );

    public string Name => "twitch.channel-event-log";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId is not Guid broadcasterId)
            return Result.Success(); // directory-level event — no channel to attribute it to

        if (!EventTypeToChannelType.TryGetValue(@event.EventType, out string? channelType))
            return Result.Success(); // not a surfaced channel fact (runner already filters, but stay safe)

        string id = @event.EventId.ToString();
        ChannelEvent? row = await db.ChannelEvents.FirstOrDefaultAsync(
            e => e.Id == id,
            cancellationToken
        );

        DateTime occurredAt = DateTime.SpecifyKind(@event.OccurredAt, DateTimeKind.Utc);

        if (row is null)
        {
            row = new ChannelEvent
            {
                Id = id,
                ChannelId = broadcasterId,
                // UserId is the actor's internal User surrogate. The fold leaves it null and snapshots the actor's
                // Twitch id into Data instead; ChannelEventActorBackfill links it in ONE set-based pass after the
                // rebuild (get-or-create Users in bulk, then a single UPDATE) — far cheaper than a per-event lookup.
                UserId = null,
                Type = channelType,
                CreatedAt = occurredAt,
            };
            db.ChannelEvents.Add(row);
        }
        else
        {
            row.ChannelId = broadcasterId;
            row.Type = channelType;
            row.CreatedAt = occurredAt;
        }

        row.Data = BuildData(@event);
        row.UpdatedAt = occurredAt;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ChannelEvent> rows = await (
            broadcasterId is Guid id
                ? db.ChannelEvents.Where(e => e.ChannelId == id)
                : db.ChannelEvents
        ).ToListAsync(cancellationToken);
        db.ChannelEvents.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // The dashboard payload: ids + a display-name snapshot + the event's salient scalar fields, lifted from the
    // journal payload (which is the serialized current-shape domain event). Free-text (chat/cheer/resub messages) is
    // dropped per F.4 ("free-text scrubbed"); only structured, renderable fields survive. The event's true occurrence
    // time travels in `occurredAt` (ISO-8601 UTC) — the legacy-shaped ChannelEvent has no OccurredAt column and the
    // audit interceptor overwrites CreatedAt with the import/write time, so the historic moment is carried here.
    private static string BuildData(EventRecord @event)
    {
        JObject source = TryParse(@event.PayloadJson);
        JObject data = new()
        {
            ["eventId"] = @event.EventId.ToString(),
            ["occurredAt"] = DateTime
                .SpecifyKind(@event.OccurredAt, DateTimeKind.Utc)
                .ToString("O"),
        };

        switch (@event.EventType)
        {
            case "ChatMessageReceivedEvent":
                Copy(
                    source,
                    data,
                    ("UserId", "userId"),
                    ("UserDisplayName", "userDisplayName"),
                    ("UserLogin", "userLogin")
                );
                CopyInt(source, data, ("Bits", "bits"));
                break;
            case "NewFollowerEvent":
            case "ModeratorAddedEvent":
            case "ModeratorRemovedEvent":
                Copy(
                    source,
                    data,
                    ("UserId", "userId"),
                    ("UserDisplayName", "userDisplayName"),
                    ("UserLogin", "userLogin")
                );
                break;
            case "NewSubscriptionEvent":
                Copy(
                    source,
                    data,
                    ("UserId", "userId"),
                    ("UserDisplayName", "userDisplayName"),
                    ("Tier", "tier")
                );
                break;
            case "ResubscriptionEvent":
                Copy(
                    source,
                    data,
                    ("UserId", "userId"),
                    ("UserDisplayName", "userDisplayName"),
                    ("Tier", "tier")
                );
                CopyInt(
                    source,
                    data,
                    ("CumulativeMonths", "cumulativeMonths"),
                    ("StreakMonths", "streakMonths")
                );
                break;
            case "SubscriptionEndedEvent":
                Copy(
                    source,
                    data,
                    ("UserId", "userId"),
                    ("UserDisplayName", "userDisplayName"),
                    ("UserLogin", "userLogin"),
                    ("Tier", "tier")
                );
                CopyBool(source, data, ("IsGift", "isGift"));
                break;
            case "GiftSubscriptionEvent":
                Copy(
                    source,
                    data,
                    ("GifterUserId", "gifterUserId"),
                    ("GifterDisplayName", "gifterDisplayName"),
                    ("Tier", "tier")
                );
                CopyInt(source, data, ("GiftCount", "giftCount"));
                CopyBool(source, data, ("IsAnonymous", "isAnonymous"));
                break;
            case "CheerEvent":
                Copy(source, data, ("UserId", "userId"), ("UserDisplayName", "userDisplayName"));
                CopyInt(source, data, ("Bits", "bits"));
                CopyBool(source, data, ("IsAnonymous", "isAnonymous"));
                break;
            case "RaidEvent":
                Copy(
                    source,
                    data,
                    ("FromUserId", "fromUserId"),
                    ("FromDisplayName", "fromDisplayName"),
                    ("FromLogin", "fromLogin")
                );
                CopyInt(source, data, ("ViewerCount", "viewerCount"));
                break;
            case "RewardRedeemedEvent":
                Copy(
                    source,
                    data,
                    ("UserId", "userId"),
                    ("UserDisplayName", "userDisplayName"),
                    ("RewardId", "rewardId"),
                    ("RewardTitle", "rewardTitle"),
                    ("RedemptionId", "redemptionId")
                );
                CopyInt(source, data, ("Cost", "cost"));
                break;
            case "UserBannedEvent":
                Copy(
                    source,
                    data,
                    ("TargetUserId", "targetUserId"),
                    ("TargetDisplayName", "targetDisplayName"),
                    ("ModeratorUserId", "moderatorUserId")
                );
                break;
            case "UserTimedOutEvent":
                Copy(
                    source,
                    data,
                    ("TargetUserId", "targetUserId"),
                    ("TargetDisplayName", "targetDisplayName"),
                    ("ModeratorUserId", "moderatorUserId")
                );
                CopyInt(source, data, ("DurationSeconds", "durationSeconds"));
                break;
        }

        // A stable actor identity spanning every event type, so ChannelEventActorBackfill can link UserId in one
        // set-based pass without re-branching on type. Anonymous/absent actors carry no id and stay unlinked.
        WriteActor(@event.EventType, source, data);

        return JsonConvert.SerializeObject(data);
    }

    // The Twitch id + display/login that name the event's actor, per event type — the chatter, the raider, the
    // gifter, the banned user — projected onto one consistent (actorTwitchUserId, actorLogin, actorDisplay) key set.
    private static readonly IReadOnlyDictionary<
        string,
        (string IdKey, string NameKey, string? LoginKey)
    > ActorByEventType = new Dictionary<string, (string, string, string?)>(StringComparer.Ordinal)
    {
        ["ChatMessageReceivedEvent"] = ("UserId", "UserDisplayName", "UserLogin"),
        ["NewFollowerEvent"] = ("UserId", "UserDisplayName", "UserLogin"),
        ["NewSubscriptionEvent"] = ("UserId", "UserDisplayName", null),
        ["ResubscriptionEvent"] = ("UserId", "UserDisplayName", null),
        ["SubscriptionEndedEvent"] = ("UserId", "UserDisplayName", "UserLogin"),
        ["GiftSubscriptionEvent"] = ("GifterUserId", "GifterDisplayName", null),
        ["CheerEvent"] = ("UserId", "UserDisplayName", null),
        ["RaidEvent"] = ("FromUserId", "FromDisplayName", "FromLogin"),
        ["RewardRedeemedEvent"] = ("UserId", "UserDisplayName", null),
        ["UserBannedEvent"] = ("TargetUserId", "TargetDisplayName", null),
        ["UserTimedOutEvent"] = ("TargetUserId", "TargetDisplayName", null),
        ["ModeratorAddedEvent"] = ("UserId", "UserDisplayName", "UserLogin"),
        ["ModeratorRemovedEvent"] = ("UserId", "UserDisplayName", "UserLogin"),
    };

    private static void WriteActor(string eventType, JObject source, JObject data)
    {
        if (
            !ActorByEventType.TryGetValue(
                eventType,
                out (string IdKey, string NameKey, string? LoginKey) f
            )
        )
            return;

        string? twitchId = source[f.IdKey]?.Value<string>();
        // "anonymous" is the anonymous-cheer/gift sentinel — there is no real viewer to link, so emit no actor id.
        if (string.IsNullOrEmpty(twitchId) || twitchId == "anonymous")
            return;

        string display = source[f.NameKey]?.Value<string>() ?? twitchId;
        string login =
            (f.LoginKey is null ? null : source[f.LoginKey]?.Value<string>())
            ?? display.ToLowerInvariant();

        data["actorTwitchUserId"] = twitchId;
        data["actorLogin"] = login;
        data["actorDisplay"] = display;
    }

    private static JObject TryParse(string json)
    {
        try
        {
            return JObject.Parse(json);
        }
        catch (JsonException)
        {
            return new JObject();
        }
    }

    private static void Copy(JObject source, JObject target, params (string From, string To)[] keys)
    {
        foreach ((string from, string to) in keys)
        {
            string? value = source[from]?.Value<string>();
            if (!string.IsNullOrEmpty(value))
                target[to] = value;
        }
    }

    private static void CopyInt(
        JObject source,
        JObject target,
        params (string From, string To)[] keys
    )
    {
        foreach ((string from, string to) in keys)
        {
            int? value = source[from]?.Value<int?>();
            if (value is int v)
                target[to] = v;
        }
    }

    private static void CopyBool(
        JObject source,
        JObject target,
        params (string From, string To)[] keys
    )
    {
        foreach ((string from, string to) in keys)
        {
            bool? value = source[from]?.Value<bool?>();
            if (value is bool v)
                target[to] = v;
        }
    }
}
