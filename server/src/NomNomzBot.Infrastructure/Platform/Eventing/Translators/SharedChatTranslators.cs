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
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Shared payload-shape reader for the shared-chat translators: all three notifications carry the same
/// <c>participants</c> array of <c>{ broadcaster_user_id, ... }</c>, so the begin / update translators read the
/// participating broadcaster ids identically (and degrade to an empty list when the array is absent).
/// </summary>
internal static class SharedChatPayload
{
    /// <summary>The <c>broadcaster_user_id</c> of every participant, empty when the array is absent / not an array.</summary>
    public static IReadOnlyList<string> ReadParticipantIds(JsonElement payload)
    {
        if (
            !payload.TryGetProperty("participants", out JsonElement participants)
            || participants.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<string> ids = new(participants.GetArrayLength());
        foreach (JsonElement participant in participants.EnumerateArray())
        {
            string id = participant.GetRequiredString("broadcaster_user_id");
            if (id.Length > 0)
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}

/// <summary>Translates <c>channel.shared_chat.begin</c> into <see cref="SharedChatBeganEvent"/>.</summary>
public sealed class ChannelSharedChatBeginTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.shared_chat.begin";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        SharedChatBeganEvent began = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            SessionId = payload.GetRequiredString("session_id"),
            HostBroadcasterId = payload.GetRequiredString("host_broadcaster_user_id"),
            HostBroadcasterDisplayName = payload.GetRequiredString("host_broadcaster_user_name"),
            HostBroadcasterLogin = payload.GetRequiredString("host_broadcaster_user_login"),
            Participants = SharedChatPayload.ReadParticipantIds(payload),
        };

        return PublishAsync(began, ct);
    }
}

/// <summary>Translates <c>channel.shared_chat.update</c> into <see cref="SharedChatUpdatedEvent"/>.</summary>
public sealed class ChannelSharedChatUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.shared_chat.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        SharedChatUpdatedEvent updated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            SessionId = payload.GetRequiredString("session_id"),
            HostBroadcasterId = payload.GetRequiredString("host_broadcaster_user_id"),
            Participants = SharedChatPayload.ReadParticipantIds(payload),
        };

        return PublishAsync(updated, ct);
    }
}

/// <summary>Translates <c>channel.shared_chat.end</c> into <see cref="SharedChatEndedEvent"/>.</summary>
public sealed class ChannelSharedChatEndTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.shared_chat.end";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        SharedChatEndedEvent ended = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            SessionId = payload.GetRequiredString("session_id"),
            HostBroadcasterId = payload.GetRequiredString("host_broadcaster_user_id"),
        };

        return PublishAsync(ended, ct);
    }
}
