// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Translates <c>channel.ban</c> into the right shape: Twitch sends one event for both a permanent ban and a
/// timeout, distinguished by <c>is_permanent</c>. When it is <c>false</c> the ban has an <c>ends_at</c> and is a
/// timeout, so this publishes <see cref="UserTimedOutEvent"/> with the remaining duration derived from
/// <c>ends_at - banned_at</c>; otherwise it publishes <see cref="UserBannedEvent"/>. Exactly one event is
/// published per notification. Payload fields: <c>user_id</c>, <c>user_name</c>, <c>moderator_user_id</c>,
/// <c>reason</c>, <c>banned_at</c>, <c>ends_at</c>, <c>is_permanent</c>.
/// </summary>
public sealed class ChannelBanTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.ban";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        string targetUserId = payload.GetRequiredString("user_id");
        string targetDisplayName = payload.GetRequiredString("user_name");
        string moderatorUserId = payload.GetRequiredString("moderator_user_id");
        string? reason = payload.GetString("reason");

        if (payload.GetBool("is_permanent"))
        {
            UserBannedEvent banned = new()
            {
                BroadcasterId = notification.BroadcasterId,
                Timestamp = Clock.GetUtcNow(),
                TargetUserId = targetUserId,
                TargetDisplayName = targetDisplayName,
                ModeratorUserId = moderatorUserId,
                Reason = reason,
            };

            return PublishAsync(banned, ct);
        }

        DateTimeOffset bannedAt = payload.GetDateTimeOffset("banned_at") ?? Clock.GetUtcNow();
        DateTimeOffset? endsAt = payload.GetDateTimeOffset("ends_at");
        int durationSeconds =
            endsAt is { } end && end > bannedAt ? (int)(end - bannedAt).TotalSeconds : 0;

        UserTimedOutEvent timedOut = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            TargetUserId = targetUserId,
            TargetDisplayName = targetDisplayName,
            ModeratorUserId = moderatorUserId,
            DurationSeconds = durationSeconds,
            Reason = reason,
        };

        return PublishAsync(timedOut, ct);
    }
}

/// <summary>
/// Translates <c>channel.unban</c> into <see cref="UserUnbannedEvent"/>. Payload fields: <c>user_id</c>,
/// <c>moderator_user_id</c>.
/// </summary>
public sealed class ChannelUnbanTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.unban";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        UserUnbannedEvent unbanned = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            TargetUserId = payload.GetRequiredString("user_id"),
            ModeratorUserId = payload.GetRequiredString("moderator_user_id"),
        };

        return PublishAsync(unbanned, ct);
    }
}

/// <summary>
/// Translates <c>channel.unban_request.create</c> into <see cref="UnbanRequestCreatedEvent"/>. Payload fields:
/// <c>id</c> (the unban-request id), <c>user_id</c>, <c>user_login</c>, <c>user_name</c>, <c>text</c>.
/// </summary>
public sealed class ChannelUnbanRequestCreateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.unban_request.create";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        UnbanRequestCreatedEvent created = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            RequestId = payload.GetRequiredString("id"),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            Text = payload.GetRequiredString("text"),
        };

        return PublishAsync(created, ct);
    }
}

/// <summary>
/// Translates <c>channel.unban_request.resolve</c> into <see cref="UnbanRequestResolvedEvent"/>. Payload fields:
/// <c>id</c>, <c>user_id</c>, <c>user_name</c>, <c>moderator_user_id</c>, <c>moderator_user_name</c>,
/// <c>resolution_text</c>, and <c>status</c> (<c>approved</c>/<c>denied</c>/<c>canceled</c>).
/// </summary>
public sealed class ChannelUnbanRequestResolveTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.unban_request.resolve";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        UnbanRequestResolvedEvent resolved = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            RequestId = payload.GetRequiredString("id"),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            ModeratorId = payload.GetRequiredString("moderator_user_id"),
            ModeratorDisplayName = payload.GetRequiredString("moderator_user_name"),
            Status = payload.GetRequiredString("status"),
            ResolutionText = payload.GetRequiredString("resolution_text"),
        };

        return PublishAsync(resolved, ct);
    }
}

/// <summary>
/// Translates <c>channel.moderator.add</c> into <see cref="ModeratorAddedEvent"/>. Payload fields: <c>user_id</c>,
/// <c>user_login</c>, <c>user_name</c>.
/// </summary>
public sealed class ChannelModeratorAddTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.moderator.add";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ModeratorAddedEvent added = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
        };

        return PublishAsync(added, ct);
    }
}

/// <summary>
/// Translates <c>channel.moderator.remove</c> into <see cref="ModeratorRemovedEvent"/>. Payload fields:
/// <c>user_id</c>, <c>user_login</c>, <c>user_name</c>.
/// </summary>
public sealed class ChannelModeratorRemoveTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.moderator.remove";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ModeratorRemovedEvent removed = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
        };

        return PublishAsync(removed, ct);
    }
}

/// <summary>
/// Translates <c>channel.vip.add</c> into <see cref="VipAddedEvent"/>. Payload fields: <c>user_id</c>,
/// <c>user_login</c>, <c>user_name</c>.
/// </summary>
public sealed class ChannelVipAddTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.vip.add";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        VipAddedEvent added = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
        };

        return PublishAsync(added, ct);
    }
}

/// <summary>
/// Translates <c>channel.vip.remove</c> into <see cref="VipRemovedEvent"/>. Payload fields: <c>user_id</c>,
/// <c>user_login</c>, <c>user_name</c>.
/// </summary>
public sealed class ChannelVipRemoveTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.vip.remove";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        VipRemovedEvent removed = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
        };

        return PublishAsync(removed, ct);
    }
}
