// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Null-tolerant parsing for the nested shapes shared by the chat translators: the <c>message.fragments[]</c> array,
/// the <c>badges[]</c> array, and the role flags Twitch only expresses through badge set ids. Lives beside the chat
/// translators because no other fan-out needs it; kept off the generic <see cref="EventSubPayload"/> readers so
/// those stay scalar-only. Every reader guards <see cref="JsonValueKind"/> so a missing/oddly-typed field degrades
/// to a safe default rather than faulting the dispatcher on the hot path.
/// </summary>
internal static class ChatPayload
{
    /// <summary>Builds the structured fragment list from a <c>message.fragments</c> array (empty when absent).</summary>
    public static IReadOnlyList<ChatMessageFragment> ReadFragments(JsonElement? messageObject)
    {
        List<ChatMessageFragment> fragments = new();
        if (
            messageObject is not { } message
            || !message.TryGetProperty("fragments", out JsonElement array)
            || array.ValueKind != JsonValueKind.Array
        )
        {
            return fragments;
        }

        foreach (JsonElement fragment in array.EnumerateArray())
        {
            fragments.Add(ReadFragment(fragment));
        }

        return fragments;
    }

    private static ChatMessageFragment ReadFragment(JsonElement fragment)
    {
        JsonElement? emote = fragment.GetObject("emote");
        JsonElement? cheermote = fragment.GetObject("cheermote");
        JsonElement? mention = fragment.GetObject("mention");

        return new ChatMessageFragment
        {
            Type = fragment.GetString("type") ?? "text",
            Text = fragment.GetRequiredString("text"),
            EmoteId = emote?.GetString("id"),
            EmoteSetId = emote?.GetString("emote_set_id"),
            EmoteOwnerId = emote?.GetString("owner_id"),
            EmoteFormats = ReadStringArray(emote, "format"),
            CheermotePrefix = cheermote?.GetString("prefix"),
            CheermoteBits = cheermote is { } c ? c.GetInt("bits") : null,
            CheermoteTier = cheermote is { } t ? t.GetInt("tier") : null,
            MentionUserId = mention?.GetString("user_id"),
            MentionUserLogin = mention?.GetString("user_login"),
            MentionUserName = mention?.GetString("user_name"),
        };
    }

    private static string[] ReadStringArray(JsonElement? owner, string name)
    {
        if (owner is not { } element || !element.TryGetProperty(name, out JsonElement array))
        {
            return [];
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> values = new();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                values.Add(item.GetString() ?? string.Empty);
            }
        }

        return values.ToArray();
    }

    /// <summary>Builds the badge list from a <c>badges</c> array (empty when absent).</summary>
    public static IReadOnlyList<ChatBadge> ReadBadges(JsonElement payload)
    {
        List<ChatBadge> badges = new();
        if (
            !payload.TryGetProperty("badges", out JsonElement array)
            || array.ValueKind != JsonValueKind.Array
        )
        {
            return badges;
        }

        foreach (JsonElement badge in array.EnumerateArray())
        {
            badges.Add(
                new ChatBadge(
                    badge.GetRequiredString("set_id"),
                    badge.GetRequiredString("id"),
                    badge.GetString("info")
                )
            );
        }

        return badges;
    }

    /// <summary>True when any badge in the list carries one of the given set ids.</summary>
    public static bool HasBadge(IReadOnlyList<ChatBadge> badges, params string[] setIds)
    {
        foreach (ChatBadge badge in badges)
        {
            foreach (string setId in setIds)
            {
                if (string.Equals(badge.SetId, setId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The plain text of a <c>message</c> object: its <c>text</c> field, else the joined fragment texts.</summary>
    public static string ReadMessageText(JsonElement? messageObject)
    {
        if (messageObject is not { } message)
        {
            return string.Empty;
        }

        string? text = message.GetString("text");
        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (
            !message.TryGetProperty("fragments", out JsonElement fragments)
            || fragments.ValueKind != JsonValueKind.Array
        )
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach (JsonElement fragment in fragments.EnumerateArray())
        {
            builder.Append(fragment.GetRequiredString("text"));
        }

        return builder.ToString();
    }
}

/// <summary>
/// Translates <c>channel.chat.message</c> into <see cref="ChatMessageReceivedEvent"/> — the hot path. Maps the
/// full payload: identity (<c>chatter_user_*</c>), text + structured <c>message.fragments</c>, <c>color</c>,
/// <c>message_type</c>, <c>badges</c> (and the role flags derived from their set ids), <c>cheer.bits</c>, and the
/// <c>reply</c> parent fields. The raw Twitch broadcaster id rides on the event for the send/reply boundary while
/// the resolved tenant comes from the dispatcher.
/// </summary>
public sealed class ChannelChatMessageTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.chat.message";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        JsonElement? message = payload.GetObject("message");
        JsonElement? cheer = payload.GetObject("cheer");
        JsonElement? reply = payload.GetObject("reply");
        IReadOnlyList<ChatBadge> badges = ChatPayload.ReadBadges(payload);

        ChatMessageReceivedEvent received = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            TwitchBroadcasterId = notification.TwitchBroadcasterUserId,
            MessageId = payload.GetRequiredString("message_id"),
            UserId = payload.GetRequiredString("chatter_user_id"),
            UserLogin = payload.GetRequiredString("chatter_user_login"),
            UserDisplayName = payload.GetRequiredString("chatter_user_name"),
            Message = ChatPayload.ReadMessageText(message),
            Fragments = ChatPayload.ReadFragments(message),
            ColorHex = payload.GetString("color"),
            MessageType = payload.GetString("message_type") ?? "text",
            Badges = badges,
            IsSubscriber = ChatPayload.HasBadge(badges, "subscriber", "founder"),
            IsVip = ChatPayload.HasBadge(badges, "vip"),
            IsModerator = ChatPayload.HasBadge(badges, "moderator"),
            IsBroadcaster = ChatPayload.HasBadge(badges, "broadcaster"),
            Bits = cheer is { } c ? c.GetInt("bits") : 0,
            ReplyParentMessageId = reply?.GetString("parent_message_id"),
            ReplyParentMessageBody = reply?.GetString("parent_message_body"),
            ReplyParentUserName = reply?.GetString("parent_user_name"),
        };

        return PublishAsync(received, ct);
    }
}

/// <summary>
/// Translates <c>channel.chat.message_delete</c> into <see cref="ChatMessageDeletedEvent"/> (a single message
/// removed by a moderator). The payload names the target chatter and the message; Twitch does not carry the acting
/// moderator on this topic, so <c>DeletedByUserId</c> is left empty.
/// </summary>
public sealed class ChannelChatMessageDeleteTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.chat.message_delete";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ChatMessageDeletedEvent deleted = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            MessageId = payload.GetRequiredString("message_id"),
            TargetUserId = payload.GetRequiredString("target_user_id"),
            DeletedByUserId = string.Empty,
        };

        return PublishAsync(deleted, ct);
    }
}

/// <summary>
/// Translates <c>channel.chat.clear</c> into <see cref="ChatClearedEvent"/> (the whole channel chat was wiped).
/// The payload carries only the broadcaster; Twitch does not name the acting moderator, so <c>ClearedByUserId</c>
/// is left empty.
/// </summary>
public sealed class ChannelChatClearTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.chat.clear";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        ChatClearedEvent cleared = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            ClearedByUserId = string.Empty,
        };

        return PublishAsync(cleared, ct);
    }
}

/// <summary>
/// Translates <c>channel.chat.clear_user_messages</c> into <see cref="ChatUserMessagesClearedEvent"/> (every
/// message from one chatter purged) — the targeted counterpart to <see cref="ChannelChatClearTranslator"/>.
/// </summary>
public sealed class ChannelChatClearUserMessagesTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.chat.clear_user_messages";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ChatUserMessagesClearedEvent cleared = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            TargetUserId = payload.GetRequiredString("target_user_id"),
            TargetUserDisplayName = payload.GetRequiredString("target_user_name"),
            TargetUserLogin = payload.GetRequiredString("target_user_login"),
        };

        return PublishAsync(cleared, ct);
    }
}

/// <summary>
/// Translates <c>channel.chat.notification</c> into <see cref="ChatNotificationEvent"/> — Twitch's unified
/// chat-notice topic. Surfaces the <c>notice_type</c> discriminator, the pre-rendered <c>system_message</c>, and
/// the chatter's own message text, rather than branching into a separate event per notice kind.
/// </summary>
public sealed class ChannelChatNotificationTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.chat.notification";

    public override async Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        JsonElement? message = payload.GetObject("message");
        string noticeType = payload.GetRequiredString("notice_type");

        ChatNotificationEvent notice = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            ChatterUserId = payload.GetRequiredString("chatter_user_id"),
            ChatterDisplayName = payload.GetRequiredString("chatter_user_name"),
            ChatterLogin = payload.GetRequiredString("chatter_user_login"),
            IsAnonymous = payload.GetBool("chatter_is_anonymous"),
            NoticeType = noticeType,
            SystemMessage = payload.GetRequiredString("system_message"),
            MessageText = ChatPayload.ReadMessageText(message),
            MessageId = payload.GetRequiredString("message_id"),
        };
        await PublishAsync(notice, ct);

        // A watch_streak notice also carries the milestone — surface it as the WatchStreakReceivedEvent the
        // WatchStreak read model folds. This is the EventSub source for watch streaks (the IRC service no longer
        // parses inbound events). watch_streak.streak_count = consecutive broadcasts watched.
        if (noticeType == "watch_streak" && payload.GetObject("watch_streak") is JsonElement streak)
            await PublishAsync(
                new WatchStreakReceivedEvent
                {
                    BroadcasterId = notification.BroadcasterId,
                    Timestamp = Clock.GetUtcNow(),
                    UserId = notice.ChatterUserId,
                    UserLogin = notice.ChatterLogin,
                    UserDisplayName = notice.ChatterDisplayName,
                    StreakMonths = streak.GetInt("streak_count"),
                    ChannelPointsEarned = streak.GetInt("channel_points_awarded"),
                },
                ct
            );
    }
}

/// <summary>
/// Translates <c>channel.chat_settings.update</c> into <see cref="ChatSettingsUpdatedEvent"/> (the channel's chat
/// moderation modes changed). The duration/wait fields are read as nullable and stay null when their mode is off.
/// </summary>
public sealed class ChannelChatSettingsUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.chat_settings.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        bool followerMode = payload.GetBool("follower_mode");
        bool slowMode = payload.GetBool("slow_mode");

        ChatSettingsUpdatedEvent settings = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            EmoteMode = payload.GetBool("emote_mode"),
            FollowerMode = followerMode,
            FollowerModeDurationMinutes = followerMode
                ? payload.GetInt("follower_mode_duration_minutes")
                : null,
            SlowMode = slowMode,
            SlowModeWaitSeconds = slowMode ? payload.GetInt("slow_mode_wait_time_seconds") : null,
            SubscriberMode = payload.GetBool("subscriber_mode"),
            UniqueChatMode = payload.GetBool("unique_chat_mode"),
        };

        return PublishAsync(settings, ct);
    }
}

/// <summary>
/// Translates <c>channel.chat.user_message_hold</c> into <see cref="ChatUserMessageHeldEvent"/> (a chatter's
/// message was held for moderator review).
/// </summary>
public sealed class ChannelChatUserMessageHoldTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.chat.user_message_hold";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ChatUserMessageHeldEvent held = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            MessageId = payload.GetRequiredString("message_id"),
            Text = ChatPayload.ReadMessageText(payload.GetObject("message")),
        };

        return PublishAsync(held, ct);
    }
}

/// <summary>
/// Translates <c>channel.chat.user_message_update</c> into <see cref="ChatUserMessageUpdatedEvent"/> (a held
/// message was approved, denied, or marked invalid). <c>status</c> carries the resolution.
/// </summary>
public sealed class ChannelChatUserMessageUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.chat.user_message_update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ChatUserMessageUpdatedEvent updated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            MessageId = payload.GetRequiredString("message_id"),
            Status = payload.GetRequiredString("status"),
            Text = ChatPayload.ReadMessageText(payload.GetObject("message")),
        };

        return PublishAsync(updated, ct);
    }
}
