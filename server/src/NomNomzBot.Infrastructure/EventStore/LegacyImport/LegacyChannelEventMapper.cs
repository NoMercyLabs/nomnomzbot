// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.EventStore.LegacyImport;

/// <summary>
/// Pure mapping from a legacy <see cref="LegacyChannelEventRow"/> to a journal <see cref="AppendEventRequest"/>
/// for the target tenant. It rebuilds the <em>current</em> domain event object from the legacy TwitchLib payload
/// and serialises THAT (not the raw legacy blob), so an imported row's <c>Payload</c> is byte-identical to what a
/// live event would have journaled — projections fold it with no special case. The <c>EventId</c> is a name-based
/// UUIDv5 over <c>(targetBroadcasterId, legacyMessageId)</c>, so re-running the import is a no-op (idempotent on
/// <c>EventId</c>) and two channels importing the same Twitch message-id never collide. Unmappable / noise types
/// return <c>null</c> (the importer skips them); there is no fabrication — every field comes from the real blob.
/// </summary>
public sealed class LegacyChannelEventMapper
{
    // A fixed, project-owned namespace for the legacy-import id space — distinct from the eventsub namespace so a
    // legacy row and a live eventsub delivery of the "same" Twitch message never derive the same EventId.
    private static readonly Guid LegacyImportNamespace = Guid.Parse(
        "b2c7a9d4-3e15-4c8a-9f06-7d1e2a3b4c5d"
    );

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        NullValueHandling = NullValueHandling.Include,
    };

    /// <summary>
    /// Maps one legacy row to an append request for <paramref name="targetBroadcasterId"/>, or <c>null</c> when the
    /// row's type is not one of the imported channel events (noise, progress ticks, definition CRUD, VIP — which the
    /// legacy bot never recorded). A row whose payload is missing the fields a mapped event requires is also skipped.
    /// </summary>
    public AppendEventRequest? Map(LegacyChannelEventRow row, Guid targetBroadcasterId)
    {
        JObject? data = TryParse(row.Data);
        if (data is null)
            return null;

        DomainEventBase? @event = row.Type switch
        {
            "channel.follow" => MapFollow(
                data,
                Envelope(row, targetBroadcasterId, data, "FollowedAt")
            ),
            "channel.subscribe" => MapSubscribe(data, Envelope(row, targetBroadcasterId, data)),
            "channel.subscription.message" => MapResub(
                data,
                Envelope(row, targetBroadcasterId, data)
            ),
            "channel.subscription.gift" => MapGift(data, Envelope(row, targetBroadcasterId, data)),
            "channel.cheer" => MapCheer(data, Envelope(row, targetBroadcasterId, data)),
            "channel.raid" => MapRaid(data, Envelope(row, targetBroadcasterId, data)),
            "channel.moderator.add" => MapModeratorAdded(
                data,
                Envelope(row, targetBroadcasterId, data)
            ),
            "channel.moderator.remove" => MapModeratorRemoved(
                data,
                Envelope(row, targetBroadcasterId, data)
            ),
            "channel.ban" => MapBan(data, Envelope(row, targetBroadcasterId, data, "BannedAt")),
            "channel.points.custom.reward.redemption.add" => MapRedemption(
                data,
                Envelope(row, targetBroadcasterId, data, "RedeemedAt")
            ),
            _ => null,
        };

        if (@event is null)
            return null;

        return new AppendEventRequest(
            EventId: @event.EventId,
            BroadcasterId: @event.BroadcasterId,
            EventType: @event.GetType().Name,
            EventVersion: 1,
            Source: "import",
            PayloadJson: JsonConvert.SerializeObject(@event, @event.GetType(), SerializerSettings),
            MetadataJson: $"{{\"legacyId\":{JsonConvert.ToString(row.Id)},\"legacyType\":{JsonConvert.ToString(row.Type)}}}",
            OccurredAt: @event.OccurredAt.UtcDateTime
        );
    }

    // The deterministic identity + tenant + event-time stamped onto every rebuilt event. Time prefers the real
    // Twitch event time inside the payload (e.g. FollowedAt/RedeemedAt), falling back to the legacy DB upsert time.
    private readonly record struct EventEnvelope(
        Guid EventId,
        Guid Tenant,
        DateTimeOffset OccurredAt
    );

    private static EventEnvelope Envelope(
        LegacyChannelEventRow row,
        Guid tenant,
        JObject data,
        string? eventTimeKey = null
    )
    {
        Guid eventId = NameBasedGuid.Version5(LegacyImportNamespace, $"{tenant:N}:{row.Id}");
        DateTime occurred = eventTimeKey is null
            ? AsUtc(row.CreatedAt)
            : EventTime(data, eventTimeKey, row);
        return new EventEnvelope(eventId, tenant, new DateTimeOffset(occurred, TimeSpan.Zero));
    }

    // ── follow ──────────────────────────────────────────────────────────────────────────────────────────────
    // The new model splits the moment of following: NewFollowerEvent is the count-bearing fact the analytics and
    // viewer projections subscribe to, so the legacy channel.follow maps to it (FollowEvent is the realtime alert
    // twin and is not journaled by the analytics fold).
    private static NewFollowerEvent? MapFollow(JObject data, EventEnvelope env)
    {
        string? userId = Str(data, "UserId");
        return userId is null
            ? null
            : new NewFollowerEvent
            {
                EventId = env.EventId,
                BroadcasterId = env.Tenant,
                OccurredAt = env.OccurredAt,
                UserId = userId,
                UserDisplayName = Str(data, "UserName") ?? userId,
                UserLogin = Str(data, "UserLogin") ?? userId,
            };
    }

    // ── subscriptions ───────────────────────────────────────────────────────────────────────────────────────
    private static NewSubscriptionEvent? MapSubscribe(JObject data, EventEnvelope env)
    {
        string? userId = Str(data, "UserId");
        // A gifted sub is journaled by its gifter through channel.subscription.gift; channel.subscribe with
        // IsGift=true is the recipient-side duplicate, so the count-bearing NewSubscriptionEvent skips it.
        if (userId is null || Bool(data, "IsGift") == true)
            return null;

        return new NewSubscriptionEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            UserId = userId,
            UserDisplayName = Str(data, "UserName") ?? userId,
            Tier = Str(data, "Tier") ?? "1000",
        };
    }

    private static ResubscriptionEvent? MapResub(JObject data, EventEnvelope env)
    {
        string? userId = Str(data, "UserId");
        return userId is null
            ? null
            : new ResubscriptionEvent
            {
                EventId = env.EventId,
                BroadcasterId = env.Tenant,
                OccurredAt = env.OccurredAt,
                UserId = userId,
                UserDisplayName = Str(data, "UserName") ?? userId,
                Tier = Str(data, "Tier") ?? "1000",
                CumulativeMonths = Int(data, "CumulativeMonths") ?? 0,
                StreakMonths = Int(data, "StreakMonths") ?? 0,
                Message = (data["Message"] as JObject)?["Text"]?.Value<string>(),
            };
    }

    private static GiftSubscriptionEvent MapGift(JObject data, EventEnvelope env)
    {
        bool anonymous = Bool(data, "IsAnonymous") ?? false;
        string gifterId = Str(data, "UserId") ?? "anonymous";

        // The legacy gift event records the gifter and a Total count but not the individual recipients (Twitch
        // delivers those as separate channel.subscribe rows). The new event models recipients as a list; an empty
        // list with the correct GiftCount preserves the count-bearing fact the subscriber projections fold.
        return new GiftSubscriptionEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            GifterUserId = anonymous ? "anonymous" : gifterId,
            GifterDisplayName = anonymous ? "Anonymous" : (Str(data, "UserName") ?? gifterId),
            Tier = Str(data, "Tier") ?? "1000",
            GiftCount = Int(data, "Total") ?? 1,
            IsAnonymous = anonymous,
            Recipients = [],
        };
    }

    // ── cheer ───────────────────────────────────────────────────────────────────────────────────────────────
    private static CheerEvent MapCheer(JObject data, EventEnvelope env)
    {
        bool anonymous = Bool(data, "IsAnonymous") ?? false;
        string userId = Str(data, "UserId") ?? "anonymous";

        return new CheerEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            UserId = anonymous ? "anonymous" : userId,
            UserDisplayName = anonymous ? "Anonymous" : (Str(data, "UserName") ?? userId),
            Bits = Int(data, "Bits") ?? 0,
            Message = Str(data, "Message") ?? string.Empty,
            IsAnonymous = anonymous,
        };
    }

    // ── raid ────────────────────────────────────────────────────────────────────────────────────────────────
    // The legacy raid row inverts the convention: ChannelId is the raid TARGET, UserId is the raider. The payload
    // carries From*/To* explicitly, so the mapper reads the raider from FromBroadcasterUser*.
    private static RaidEvent? MapRaid(JObject data, EventEnvelope env)
    {
        string? fromId = Str(data, "FromBroadcasterUserId");
        return fromId is null
            ? null
            : new RaidEvent
            {
                EventId = env.EventId,
                BroadcasterId = env.Tenant,
                OccurredAt = env.OccurredAt,
                FromUserId = fromId,
                FromDisplayName = Str(data, "FromBroadcasterUserName") ?? fromId,
                FromLogin = Str(data, "FromBroadcasterUserLogin") ?? fromId,
                ViewerCount = Int(data, "Viewers") ?? 0,
            };
    }

    // ── moderator roster ────────────────────────────────────────────────────────────────────────────────────
    private static ModeratorAddedEvent? MapModeratorAdded(JObject data, EventEnvelope env)
    {
        string? userId = Str(data, "UserId");
        return userId is null
            ? null
            : new ModeratorAddedEvent
            {
                EventId = env.EventId,
                BroadcasterId = env.Tenant,
                OccurredAt = env.OccurredAt,
                UserId = userId,
                UserDisplayName = Str(data, "UserName") ?? userId,
                UserLogin = Str(data, "UserLogin") ?? userId,
            };
    }

    private static ModeratorRemovedEvent? MapModeratorRemoved(JObject data, EventEnvelope env)
    {
        string? userId = Str(data, "UserId");
        return userId is null
            ? null
            : new ModeratorRemovedEvent
            {
                EventId = env.EventId,
                BroadcasterId = env.Tenant,
                OccurredAt = env.OccurredAt,
                UserId = userId,
                UserDisplayName = Str(data, "UserName") ?? userId,
                UserLogin = Str(data, "UserLogin") ?? userId,
            };
    }

    // ── ban / timeout ───────────────────────────────────────────────────────────────────────────────────────
    // The legacy channel.ban row covers both: a permanent ban (IsPermanent=true / EndsAt=null) maps to
    // UserBannedEvent; a temporary one (EndsAt set) maps to UserTimedOutEvent with the derived duration.
    private static DomainEventBase? MapBan(JObject data, EventEnvelope env)
    {
        string? targetId = Str(data, "UserId");
        if (targetId is null)
            return null;

        string moderatorId = Str(data, "ModeratorUserId") ?? string.Empty;
        string targetDisplay = Str(data, "UserName") ?? targetId;
        string? reason = Str(data, "Reason");

        bool permanent =
            Bool(data, "IsPermanent") ?? data["EndsAt"]?.Type is null or JTokenType.Null;
        if (permanent)
            return new UserBannedEvent
            {
                EventId = env.EventId,
                BroadcasterId = env.Tenant,
                OccurredAt = env.OccurredAt,
                TargetUserId = targetId,
                TargetDisplayName = targetDisplay,
                ModeratorUserId = moderatorId,
                Reason = reason,
            };

        DateTimeOffset? endsAt = ReadDateTimeOffset(data, "EndsAt");
        int durationSeconds = endsAt is { } e
            ? Math.Max(0, (int)(e - env.OccurredAt).TotalSeconds)
            : 0;

        return new UserTimedOutEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            TargetUserId = targetId,
            TargetDisplayName = targetDisplay,
            ModeratorUserId = moderatorId,
            DurationSeconds = durationSeconds,
            Reason = reason,
        };
    }

    // ── channel-point redemption ────────────────────────────────────────────────────────────────────────────
    private static RewardRedeemedEvent? MapRedemption(JObject data, EventEnvelope env)
    {
        string? userId = Str(data, "UserId");
        JObject? reward = data["Reward"] as JObject;
        string? rewardId = reward?["Id"]?.Value<string>();
        if (userId is null || rewardId is null)
            return null;

        return new RewardRedeemedEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            RewardId = rewardId,
            RewardTitle = reward!["Title"]?.Value<string>() ?? string.Empty,
            RedemptionId = Str(data, "Id") ?? env.EventId.ToString(),
            UserId = userId,
            UserDisplayName = Str(data, "UserName") ?? userId,
            Cost = reward["Cost"]?.Value<int?>() ?? 0,
            UserInput = Str(data, "UserInput"),
        };
    }

    // ── payload helpers ─────────────────────────────────────────────────────────────────────────────────────
    private static JObject? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JObject.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JObject o, string key)
    {
        string? v = o[key]?.Value<string>();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static int? Int(JObject o, string key) => o[key]?.Value<int?>();

    private static bool? Bool(JObject o, string key) => o[key]?.Value<bool?>();

    private static DateTimeOffset? ReadDateTimeOffset(JObject o, string key)
    {
        JToken? token = o[key];
        if (token is null || token.Type == JTokenType.Null)
            return null;
        // Read through DateTime then widen: a JSON.NET Date token round-trips as DateTime, and casting it straight
        // to DateTimeOffset throws (InvalidCast). A string token falls through to the parse.
        if (token.Type == JTokenType.Date)
            return new DateTimeOffset(token.Value<DateTime>().ToUniversalTime());
        return DateTimeOffset.TryParse(token.Value<string>(), out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(
            value.Kind == DateTimeKind.Unspecified ? value : value.ToUniversalTime(),
            DateTimeKind.Utc
        );

    // Prefer the real Twitch event time inside the payload; fall back to the legacy DB upsert time.
    private static DateTime EventTime(JObject data, string key, LegacyChannelEventRow row) =>
        data[key]?.Type == JTokenType.Date ? AsUtc(data[key]!.Value<DateTime>())
        : DateTime.TryParse(Str(data, key), out DateTime parsed) ? AsUtc(parsed)
        : AsUtc(row.CreatedAt);
}
