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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>Translates <c>channel.guest_star_session.begin</c> into <see cref="GuestStarSessionBeganEvent"/>.</summary>
public sealed class ChannelGuestStarSessionBeginTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.guest_star_session.begin";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        GuestStarSessionBeganEvent began = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            SessionId = payload.GetRequiredString("session_id"),
            StartedAt = payload.GetDateTimeOffset("started_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(began, ct);
    }
}

/// <summary>Translates <c>channel.guest_star_session.end</c> into <see cref="GuestStarSessionEndedEvent"/>.</summary>
public sealed class ChannelGuestStarSessionEndTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.guest_star_session.end";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        DateTimeOffset endedAt = payload.GetDateTimeOffset("ended_at") ?? Clock.GetUtcNow();
        GuestStarSessionEndedEvent ended = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            SessionId = payload.GetRequiredString("session_id"),
            StartedAt = payload.GetDateTimeOffset("started_at") ?? endedAt,
            EndedAt = endedAt,
        };

        return PublishAsync(ended, ct);
    }
}

/// <summary>
/// Translates <c>channel.guest_star_guest.update</c> into <see cref="GuestStarGuestUpdatedEvent"/>. The guest and
/// moderator identity fields are situational (a vacated slot carries no guest), so they degrade to <c>null</c>;
/// only <c>session_id</c> and <c>state</c> are always present.
/// </summary>
public sealed class ChannelGuestStarGuestUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.guest_star_guest.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        GuestStarGuestUpdatedEvent updated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            SessionId = payload.GetRequiredString("session_id"),
            ModeratorId = payload.GetString("moderator_user_id"),
            GuestUserId = payload.GetString("guest_user_id"),
            GuestDisplayName = payload.GetString("guest_user_name"),
            GuestLogin = payload.GetString("guest_user_login"),
            State = payload.GetRequiredString("state"),
            SlotId = payload.GetString("slot_id"),
        };

        return PublishAsync(updated, ct);
    }
}

/// <summary>
/// Translates <c>channel.guest_star_settings.update</c> into <see cref="GuestStarSettingsUpdatedEvent"/> (the
/// channel's slot count, moderator-go-live toggle, browser-source audio toggle, and group layout).
/// </summary>
public sealed class ChannelGuestStarSettingsUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.guest_star_settings.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        GuestStarSettingsUpdatedEvent updated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            IsModeratorSendLiveEnabled = payload.GetBool("is_moderator_send_live_enabled"),
            SlotCount = payload.GetInt("slot_count"),
            IsBrowserSourceAudioEnabled = payload.GetBool("is_browser_source_audio_enabled"),
            GroupLayout = payload.GetRequiredString("group_layout"),
        };

        return PublishAsync(updated, ct);
    }
}
