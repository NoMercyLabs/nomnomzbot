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
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
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
/// live event would have journaled — projections fold it with no special case.
/// <para>
/// The <c>EventId</c> prefers the <b>real Twitch event id</b> when the payload carries one: the chat
/// <c>MessageId</c> GUID (chat message + chat notification) and the channel-points redemption <c>Id</c> GUID. Using
/// the real id means a live re-delivery of the same message/redemption dedupes against the imported one. The
/// remaining EventSub topics (follow, sub, cheer, raid, ban, moderator roster) carry <em>no</em> event-level GUID
/// in their payload — Twitch's only stable id for them is the EventSub message-id (the base64 legacy row id) — so
/// those fall back to a name-based UUIDv5 over <c>(targetBroadcasterId, legacyRowId)</c>. Either way the identity
/// is deterministic, so re-running the import is a no-op (idempotent on <c>EventId</c>) and two channels importing
/// the same id never collide. Unmappable / noise rows return <c>null</c> (the importer skips them, logging the
/// reason); there is no fabrication — every field comes from the real blob.
/// </para>
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
            // Chat message + chat notification (the ~28k bulk) — both keyed on the real Twitch MessageId GUID.
            "channel.chat.message" => MapChatMessage(
                data,
                Envelope(row, targetBroadcasterId, data, realIdKey: "MessageId")
            ),
            "channel.chat.notification" => MapChatNotification(
                data,
                Envelope(row, targetBroadcasterId, data, realIdKey: "MessageId")
            ),
            "channel.follow" => MapFollow(
                data,
                Envelope(row, targetBroadcasterId, data, eventTimeKey: "FollowedAt")
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
            "channel.ban" => MapBan(
                data,
                Envelope(row, targetBroadcasterId, data, eventTimeKey: "BannedAt")
            ),
            // The redemption carries its own real Twitch redemption GUID (`Id`) — use it as the identity.
            "channel.points.custom.reward.redemption.add" => MapRedemption(
                data,
                Envelope(
                    row,
                    targetBroadcasterId,
                    data,
                    eventTimeKey: "RedeemedAt",
                    realIdKey: "Id"
                )
            ),
            // A redemption STATUS change (fulfilled / canceled). Its payload Id is the SAME redemption id as the
            // matching .add, so it must NOT reuse realIdKey:"Id" — that would derive the add's EventId and be
            // deduped away. The deterministic per-legacy-row id keeps the status transition a distinct event.
            "channel.points.custom.reward.redemption.update" => MapRedemptionUpdate(
                data,
                Envelope(row, targetBroadcasterId, data, eventTimeKey: "RedeemedAt")
            ),
            // A channel-info change (title / category). The payload carries no real Twitch event id, so it takes
            // the deterministic per-legacy-row id and the legacy capture time.
            "channel.update" => MapChannelUpdate(data, Envelope(row, targetBroadcasterId, data)),
            // Stream lifecycle — go-live and end. Both keyed on the legacy row id (no event GUID in the payload).
            "stream.online" => MapStreamOnline(
                data,
                Envelope(row, targetBroadcasterId, data, eventTimeKey: "StartedAt")
            ),
            "stream.offline" => MapStreamOffline(data, Envelope(row, targetBroadcasterId, data)),
            // Shoutouts — created (we shout out another channel) and received (another shouts us out).
            "channel.shoutout.create" => MapShoutoutCreate(
                data,
                Envelope(row, targetBroadcasterId, data, eventTimeKey: "StartedAt")
            ),
            "channel.shoutout.receive" => MapShoutoutReceive(
                data,
                Envelope(row, targetBroadcasterId, data, eventTimeKey: "StartedAt")
            ),
            // An ad break starting.
            "channel.ad.break.begin" => MapAdBreakBegin(
                data,
                Envelope(row, targetBroadcasterId, data, eventTimeKey: "StartedAt")
            ),
            // Polls — begin / live-progress / terminal end.
            "channel.poll.begin" => MapPollBegan(
                data,
                Envelope(row, targetBroadcasterId, data, eventTimeKey: "StartedAt")
            ),
            "channel.poll.progress" => MapPollProgress(
                data,
                Envelope(row, targetBroadcasterId, data)
            ),
            "channel.poll.end" => MapPollEnded(data, Envelope(row, targetBroadcasterId, data)),
            // Hype trains — begin / progress / end (EventSub v2 shape, TwitchLib PascalCase keys).
            "channel.hype_train.begin" => MapHypeTrainBegan(
                data,
                Envelope(row, targetBroadcasterId, data, eventTimeKey: "StartedAt")
            ),
            "channel.hype_train.progress" => MapHypeTrainProgress(
                data,
                Envelope(row, targetBroadcasterId, data)
            ),
            "channel.hype_train.end" => MapHypeTrainEnded(
                data,
                Envelope(row, targetBroadcasterId, data, eventTimeKey: "EndedAt")
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
        string? eventTimeKey = null,
        string? realIdKey = null
    )
    {
        // Prefer the real Twitch event GUID inside the payload (chat MessageId / redemption Id) as the identity;
        // fall back to a deterministic UUIDv5 over (tenant, legacy row id) for the GUID-less EventSub topics.
        Guid eventId =
            realIdKey is not null && TryGuid(data, realIdKey) is { } realId
                ? realId
                : NameBasedGuid.Version5(LegacyImportNamespace, $"{tenant:N}:{row.Id}");
        DateTime occurred = eventTimeKey is null
            ? AsUtc(row.CreatedAt)
            : EventTime(data, eventTimeKey, row);
        return new EventEnvelope(eventId, tenant, new DateTimeOffset(occurred, TimeSpan.Zero));
    }

    // ── chat message / notification ─────────────────────────────────────────────────────────────────────────
    // The legacy chat blob is the TwitchLib channel.chat.message shape: ChatterUser* identity, a nested
    // Message{Text,Fragments[]}, Badges[], Color, MessageType, Cheer{Bits}, Reply{Parent*}. We rebuild the
    // current ChatMessageReceivedEvent from it so every chat-folding projection (viewer profiles, daily activity,
    // engagement) folds it exactly like a live capture — which is what makes distinct viewers > 0 after rebuild.
    private static ChatMessageReceivedEvent? MapChatMessage(JObject data, EventEnvelope env)
    {
        string? chatterId = Str(data, "ChatterUserId");
        if (chatterId is null)
            return null;

        JObject? message = data["Message"] as JObject;
        IReadOnlyList<ChatBadge> badges = ReadBadges(data);
        JObject? cheer = data["Cheer"] as JObject;
        JObject? reply = data["Reply"] as JObject;

        return new ChatMessageReceivedEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            // The raw broadcaster string id rides on the event for the send/reply boundary, as on the live path.
            TwitchBroadcasterId = Str(data, "BroadcasterUserId") ?? string.Empty,
            MessageId = Str(data, "MessageId") ?? env.EventId.ToString(),
            UserId = chatterId,
            UserDisplayName = Str(data, "ChatterUserName") ?? chatterId,
            UserLogin = Str(data, "ChatterUserLogin") ?? chatterId,
            Message = ReadMessageText(message),
            Fragments = ReadFragments(message),
            ColorHex = Str(data, "Color"),
            MessageType = Str(data, "MessageType") ?? "text",
            Badges = badges,
            IsSubscriber = Bool(data, "IsSubscriber") ?? HasBadge(badges, "subscriber", "founder"),
            IsVip = Bool(data, "IsVip") ?? HasBadge(badges, "vip"),
            IsModerator = Bool(data, "IsModerator") ?? HasBadge(badges, "moderator"),
            IsBroadcaster = Bool(data, "IsBroadcaster") ?? HasBadge(badges, "broadcaster"),
            Bits = cheer is null ? 0 : Int(cheer, "Bits") ?? 0,
            ReplyParentMessageId = reply is null ? null : Str(reply, "ParentMessageId"),
            ReplyParentMessageBody = reply is null ? null : Str(reply, "ParentMessageBody"),
            ReplyParentUserName = reply is null ? null : Str(reply, "ParentUserName"),
        };
    }

    // A chat notification (announcement, watch_streak, sub/raid notice, …) is a chat-rendered system line that a
    // viewer sees in chat. It maps to a chat message — surfacing in the feed and crediting the chatter — WITHOUT
    // folding into sub/raid analytics (those are already counted by the dedicated channel.subscribe/raid topics, so
    // re-counting them here would double-count). An anonymous notice has no chatter to attribute and is skipped.
    private static ChatMessageReceivedEvent? MapChatNotification(JObject data, EventEnvelope env)
    {
        string? chatterId = Str(data, "ChatterUserId");
        if (chatterId is null)
            return null;

        JObject? message = data["Message"] as JObject;
        IReadOnlyList<ChatBadge> badges = ReadBadges(data);

        return new ChatMessageReceivedEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            TwitchBroadcasterId = Str(data, "BroadcasterUserId") ?? string.Empty,
            MessageId = Str(data, "MessageId") ?? env.EventId.ToString(),
            UserId = chatterId,
            UserDisplayName = Str(data, "ChatterUserName") ?? chatterId,
            UserLogin = Str(data, "ChatterUserLogin") ?? chatterId,
            Message = ReadMessageText(message),
            Fragments = ReadFragments(message),
            ColorHex = Str(data, "Color"),
            MessageType = "text",
            Badges = badges,
            IsSubscriber = HasBadge(badges, "subscriber", "founder"),
            IsVip = HasBadge(badges, "vip"),
            IsModerator = HasBadge(badges, "moderator"),
            IsBroadcaster = HasBadge(badges, "broadcaster"),
        };
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

    // A redemption STATUS transition (channel.points.custom.reward.redemption.update) — a queued redemption marked
    // fulfilled or canceled. Same Reward + viewer shape as the .add; surfaces the new Status. Skips a payload with
    // no viewer/reward (anonymous/garbage), like the .add does.
    private static RewardRedemptionUpdatedEvent? MapRedemptionUpdate(
        JObject data,
        EventEnvelope env
    )
    {
        string? userId = Str(data, "UserId");
        JObject? reward = data["Reward"] as JObject;
        string? rewardId = reward?["Id"]?.Value<string>();
        if (userId is null || rewardId is null)
            return null;

        return new RewardRedemptionUpdatedEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            RedemptionId = Str(data, "Id") ?? env.EventId.ToString(),
            RewardId = rewardId,
            RewardTitle = reward!["Title"]?.Value<string>() ?? string.Empty,
            UserId = userId,
            UserDisplayName = Str(data, "UserName") ?? userId,
            Status = Str(data, "Status") ?? "unknown",
        };
    }

    // A channel-info change (channel.update) — the broadcaster's stream title and/or category changed. Always
    // valid (no viewer to attribute); missing fields default to empty rather than skipping the event.
    private static ChannelUpdatedEvent MapChannelUpdate(JObject data, EventEnvelope env) =>
        new()
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            BroadcasterDisplayName = Str(data, "BroadcasterUserName") ?? string.Empty,
            NewTitle = Str(data, "Title") ?? string.Empty,
            NewGameName = Str(data, "CategoryName") ?? string.Empty,
        };

    // A stream going live (stream.online). The legacy payload carries no title/category (those ride channel.update),
    // so they default to empty; StartedAt is the real Twitch go-live time (the same as OccurredAt here).
    private static ChannelOnlineEvent MapStreamOnline(JObject data, EventEnvelope env) =>
        new()
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            BroadcasterDisplayName = Str(data, "BroadcasterUserName") ?? string.Empty,
            StreamTitle = Str(data, "Title") ?? string.Empty,
            GameName = Str(data, "CategoryName") ?? string.Empty,
            StartedAt = env.OccurredAt,
        };

    // A stream ending (stream.offline). The payload has no duration and the mapper is stateless (it cannot pair
    // this with its stream.online), so StreamDuration is zero — a stream-session projection derives the real span
    // from the online/offline OccurredAt pair on replay. The end event itself is what must be journaled.
    private static ChannelOfflineEvent MapStreamOffline(JObject data, EventEnvelope env) =>
        new()
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            BroadcasterDisplayName = Str(data, "BroadcasterUserName") ?? string.Empty,
            StreamDuration = TimeSpan.Zero,
        };

    // The broadcaster shouting OUT another channel (channel.shoutout.create) — the credit goes to the target.
    private static ShoutoutSentEvent? MapShoutoutCreate(JObject data, EventEnvelope env)
    {
        string? toId = Str(data, "ToBroadcasterUserId");
        if (toId is null)
            return null;

        return new ShoutoutSentEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            ToUserId = toId,
            ToDisplayName = Str(data, "ToBroadcasterUserName") ?? toId,
        };
    }

    // The broadcaster RECEIVING a shoutout from another channel (channel.shoutout.receive).
    private static ShoutoutReceivedEvent? MapShoutoutReceive(JObject data, EventEnvelope env)
    {
        string? fromId = Str(data, "FromBroadcasterUserId");
        if (fromId is null)
            return null;

        return new ShoutoutReceivedEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            FromBroadcasterId = fromId,
            FromBroadcasterDisplayName = Str(data, "FromBroadcasterUserName") ?? fromId,
            FromBroadcasterLogin = Str(data, "FromBroadcasterUserLogin") ?? fromId,
            ViewerCount = Int(data, "ViewerCount") ?? 0,
        };
    }

    // An ad break starting (channel.ad.break.begin). The requester is optional (an automatic break has none).
    private static AdBreakBeganEvent MapAdBreakBegin(JObject data, EventEnvelope env) =>
        new()
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            DurationSeconds = Int(data, "DurationSeconds") ?? 0,
            IsAutomatic = Bool(data, "IsAutomatic") ?? false,
            StartedAt = env.OccurredAt,
            RequesterUserId = Str(data, "RequesterUserId"),
            RequesterDisplayName = Str(data, "RequesterUserName"),
        };

    // ── polls ───────────────────────────────────────────────────────────────────────────────────────────────
    // TwitchLib PascalCase poll shape: Id, Title, Choices[]{Id,Title,Votes,ChannelPointsVotes}, DurationSeconds,
    // StartedAt, EndsAt, Status (end only). Begin uses StartedAt to derive DurationSeconds when the raw field is 0.
    private static PollBeganEvent? MapPollBegan(JObject data, EventEnvelope env)
    {
        string? pollId = Str(data, "Id");
        string? title = Str(data, "Title");
        if (pollId is null || title is null)
            return null;

        int durationSeconds = Int(data, "DurationSeconds") ?? 0;
        DateTimeOffset? endsAt = ReadDateTimeOffset(data, "EndsAt");
        if (durationSeconds == 0 && endsAt is { } e)
            durationSeconds = Math.Max(0, (int)(e - env.OccurredAt).TotalSeconds);

        return new PollBeganEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            PollId = pollId,
            Title = title,
            Choices = ReadPollChoices(data),
            DurationSeconds = durationSeconds,
            EndsAt = endsAt ?? env.OccurredAt,
        };
    }

    private static PollProgressEvent? MapPollProgress(JObject data, EventEnvelope env)
    {
        string? pollId = Str(data, "Id");
        string? title = Str(data, "Title");
        if (pollId is null || title is null)
            return null;

        return new PollProgressEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            PollId = pollId,
            Title = title,
            Choices = ReadPollChoices(data),
            EndsAt = ReadDateTimeOffset(data, "EndsAt") ?? env.OccurredAt,
        };
    }

    private static PollEndedEvent? MapPollEnded(JObject data, EventEnvelope env)
    {
        string? pollId = Str(data, "Id");
        string? title = Str(data, "Title");
        if (pollId is null || title is null)
            return null;

        IReadOnlyList<PollChoice> choices = ReadPollChoices(data);
        PollChoice? winner = null;
        foreach (PollChoice choice in choices)
        {
            if (choice.Votes > 0 && (winner is null || choice.Votes > winner.Votes))
                winner = choice;
        }

        return new PollEndedEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            PollId = pollId,
            Title = title,
            Status = Str(data, "Status") ?? "completed",
            Choices = choices,
            WinningChoiceId = winner?.Id,
        };
    }

    private static IReadOnlyList<PollChoice> ReadPollChoices(JObject data)
    {
        if (data["Choices"] is not JArray array)
            return [];

        List<PollChoice> result = new(array.Count);
        foreach (JToken token in array)
        {
            if (token is not JObject choice)
                continue;
            string? id = Str(choice, "Id");
            string? title = Str(choice, "Title");
            if (id is null || title is null)
                continue;
            result.Add(
                new PollChoice(
                    id,
                    title,
                    Int(choice, "Votes") ?? 0,
                    Int(choice, "ChannelPointsVotes") ?? 0
                )
            );
        }

        return result;
    }

    // ── hype trains ─────────────────────────────────────────────────────────────────────────────────────────
    // TwitchLib PascalCase hype-train shape: Id, Level, Total, Progress, Goal,
    // TopContributions[]{UserId,UserLogin,UserName,Type,Total}, ExpiresAt (begin/progress), EndedAt (end).
    private static HypeTrainBeganEvent? MapHypeTrainBegan(JObject data, EventEnvelope env)
    {
        string? id = Str(data, "Id");
        if (id is null)
            return null;

        return new HypeTrainBeganEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            HypeTrainId = id,
            Level = Int(data, "Level") ?? 1,
            Total = Int(data, "Total") ?? 0,
            Progress = Int(data, "Progress") ?? 0,
            Goal = Int(data, "Goal") ?? 0,
            TopContributions = ReadTopContributions(data),
            ExpiresAt = ReadDateTimeOffset(data, "ExpiresAt") ?? env.OccurredAt,
        };
    }

    private static HypeTrainProgressEvent? MapHypeTrainProgress(JObject data, EventEnvelope env)
    {
        string? id = Str(data, "Id");
        if (id is null)
            return null;

        return new HypeTrainProgressEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            HypeTrainId = id,
            Level = Int(data, "Level") ?? 1,
            Total = Int(data, "Total") ?? 0,
            Progress = Int(data, "Progress") ?? 0,
            Goal = Int(data, "Goal") ?? 0,
            TopContributions = ReadTopContributions(data),
            ExpiresAt = ReadDateTimeOffset(data, "ExpiresAt") ?? env.OccurredAt,
        };
    }

    private static HypeTrainEndedEvent? MapHypeTrainEnded(JObject data, EventEnvelope env)
    {
        string? id = Str(data, "Id");
        if (id is null)
            return null;

        return new HypeTrainEndedEvent
        {
            EventId = env.EventId,
            BroadcasterId = env.Tenant,
            OccurredAt = env.OccurredAt,
            HypeTrainId = id,
            Level = Int(data, "Level") ?? 1,
            Total = Int(data, "Total") ?? 0,
            TopContributions = ReadTopContributions(data),
            EndedAt = ReadDateTimeOffset(data, "EndedAt") ?? env.OccurredAt,
        };
    }

    private static IReadOnlyList<HypeTrainContribution> ReadTopContributions(JObject data)
    {
        if (data["TopContributions"] is not JArray array)
            return [];

        List<HypeTrainContribution> result = new(array.Count);
        foreach (JToken token in array)
        {
            if (token is not JObject contribution)
                continue;
            string? userId = Str(contribution, "UserId");
            if (userId is null)
                continue;
            result.Add(
                new HypeTrainContribution(
                    userId,
                    Str(contribution, "UserLogin") ?? userId,
                    Str(contribution, "UserName") ?? userId,
                    Str(contribution, "Type") ?? "other",
                    Int(contribution, "Total") ?? 0
                )
            );
        }

        return result;
    }

    // ── chat payload helpers ────────────────────────────────────────────────────────────────────────────────
    // The plain text of a legacy Message object: its Text, else the joined fragment texts (mirrors the live reader).
    private static string ReadMessageText(JObject? message)
    {
        if (message is null)
            return string.Empty;

        string? text = message["Text"]?.Value<string>();
        if (!string.IsNullOrEmpty(text))
            return text;

        if (message["Fragments"] is not JArray fragments)
            return string.Empty;

        return string.Concat(fragments.Select(f => f["Text"]?.Value<string>() ?? string.Empty));
    }

    // Rebuilds the structured fragment list from the legacy Message.Fragments[] (TwitchLib PascalCase, nested
    // Emote/Cheermote/Mention objects), matching the shape the live ChatMessageReceivedEvent carries.
    private static IReadOnlyList<ChatMessageFragment> ReadFragments(JObject? message)
    {
        if (message?["Fragments"] is not JArray array)
            return [];

        List<ChatMessageFragment> fragments = new(array.Count);
        foreach (JToken token in array)
        {
            if (token is not JObject fragment)
                continue;

            JObject? emote = fragment["Emote"] as JObject;
            JObject? cheermote = fragment["Cheermote"] as JObject;
            JObject? mention = fragment["Mention"] as JObject;

            fragments.Add(
                new ChatMessageFragment
                {
                    Type = fragment["Type"]?.Value<string>() ?? "text",
                    Text = fragment["Text"]?.Value<string>() ?? string.Empty,
                    EmoteId = emote?["Id"]?.Value<string>(),
                    EmoteSetId = emote?["EmoteSetId"]?.Value<string>(),
                    EmoteOwnerId = emote?["OwnerId"]?.Value<string>(),
                    EmoteFormats =
                        (emote?["Format"] as JArray)
                            ?.Select(f => f.Value<string>() ?? string.Empty)
                            .ToArray()
                        ?? [],
                    CheermotePrefix = cheermote?["Prefix"]?.Value<string>(),
                    CheermoteBits = cheermote?["Bits"]?.Value<int?>(),
                    CheermoteTier = cheermote?["Tier"]?.Value<int?>(),
                    MentionUserId = mention?["UserId"]?.Value<string>(),
                    MentionUserLogin = mention?["UserLogin"]?.Value<string>(),
                    MentionUserName = mention?["UserName"]?.Value<string>(),
                }
            );
        }

        return fragments;
    }

    private static IReadOnlyList<ChatBadge> ReadBadges(JObject data)
    {
        if (data["Badges"] is not JArray array)
            return [];

        List<ChatBadge> badges = new(array.Count);
        foreach (JToken token in array)
        {
            if (token is not JObject badge)
                continue;
            string? setId = badge["SetId"]?.Value<string>();
            string? id = badge["Id"]?.Value<string>();
            if (setId is null || id is null)
                continue;
            badges.Add(new ChatBadge(setId, id, badge["Info"]?.Value<string>()));
        }

        return badges;
    }

    private static bool HasBadge(IReadOnlyList<ChatBadge> badges, params string[] setIds) =>
        badges.Any(b => setIds.Contains(b.SetId, StringComparer.Ordinal));

    // Reads a payload field as a Guid when it is a well-formed GUID string, else null (so the caller falls back to
    // the derived UUIDv5). Guards against a non-GUID legacy id sneaking into the journal EventId.
    private static Guid? TryGuid(JObject o, string key) =>
        Guid.TryParse(o[key]?.Value<string>(), out Guid value) ? value : null;

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
