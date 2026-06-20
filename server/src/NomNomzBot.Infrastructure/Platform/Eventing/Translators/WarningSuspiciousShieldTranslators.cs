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
/// Translates <c>channel.warning.acknowledge</c> into <see cref="WarningAcknowledgedEvent"/>. Payload fields:
/// <c>user_id</c>, <c>user_login</c>, <c>user_name</c>.
/// </summary>
public sealed class ChannelWarningAcknowledgeTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.warning.acknowledge";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        WarningAcknowledgedEvent acknowledged = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
        };

        return PublishAsync(acknowledged, ct);
    }
}

/// <summary>
/// Translates <c>channel.warning.send</c> into <see cref="WarningSentEvent"/>. Payload fields:
/// <c>moderator_user_id</c>, <c>moderator_user_name</c>, <c>user_id</c>, <c>user_login</c>, <c>user_name</c>,
/// <c>reason</c> (nullable), and <c>chat_rules_cited</c> (a nullable string array → empty when absent).
/// </summary>
public sealed class ChannelWarningSendTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.warning.send";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        WarningSentEvent sent = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            ModeratorId = payload.GetRequiredString("moderator_user_id"),
            ModeratorDisplayName = payload.GetRequiredString("moderator_user_name"),
            Reason = payload.GetString("reason"),
            ChatRulesCited = ReadStringArray(payload, "chat_rules_cited"),
        };

        return PublishAsync(sent, ct);
    }

    /// <summary>
    /// Reads a nullable JSON string array, degrading to an empty list when the field is absent, null, or not an
    /// array — keeping the translator on the hot path the same way the shared scalar readers do.
    /// </summary>
    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (
            !element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                values.Add(item.GetString() ?? string.Empty);
            }
        }

        return values;
    }
}

/// <summary>
/// Translates <c>channel.suspicious_user.message</c> into <see cref="SuspiciousUserMessageEvent"/>. Payload
/// fields: <c>user_id</c>, <c>user_login</c>, <c>user_name</c>, <c>low_trust_status</c>
/// (<c>active_monitoring</c>/<c>restricted</c>), <c>ban_evasion_evaluation</c>
/// (<c>likely</c>/<c>possible</c>/<c>unlikely</c>), and the nested <c>message</c> object's <c>message_id</c> and
/// <c>text</c>.
/// </summary>
public sealed class ChannelSuspiciousUserMessageTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.suspicious_user.message";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        JsonElement? message = payload.GetObject("message");
        SuspiciousUserMessageEvent suspicious = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            LowTrustStatus = payload.GetRequiredString("low_trust_status"),
            BanEvasionEvaluation = payload.GetRequiredString("ban_evasion_evaluation"),
            MessageId = message?.GetRequiredString("message_id") ?? string.Empty,
            Text = message?.GetRequiredString("text") ?? string.Empty,
        };

        return PublishAsync(suspicious, ct);
    }
}

/// <summary>
/// Translates <c>channel.suspicious_user.update</c> into <see cref="SuspiciousUserUpdatedEvent"/>. Payload
/// fields: <c>user_id</c>, <c>user_login</c>, <c>user_name</c>, <c>moderator_user_id</c>,
/// <c>moderator_user_name</c>, <c>low_trust_status</c> (<c>none</c>/<c>active_monitoring</c>/<c>restricted</c>).
/// </summary>
public sealed class ChannelSuspiciousUserUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.suspicious_user.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        SuspiciousUserUpdatedEvent updated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            ModeratorId = payload.GetRequiredString("moderator_user_id"),
            ModeratorDisplayName = payload.GetRequiredString("moderator_user_name"),
            LowTrustStatus = payload.GetRequiredString("low_trust_status"),
        };

        return PublishAsync(updated, ct);
    }
}

/// <summary>
/// Translates <c>channel.shield_mode.begin</c> into <see cref="ShieldModeBeganEvent"/>. Payload fields:
/// <c>moderator_user_id</c>, <c>moderator_user_name</c>, <c>started_at</c>.
/// </summary>
public sealed class ChannelShieldModeBeginTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.shield_mode.begin";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ShieldModeBeganEvent began = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            ModeratorId = payload.GetRequiredString("moderator_user_id"),
            ModeratorDisplayName = payload.GetRequiredString("moderator_user_name"),
            StartedAt = payload.GetDateTimeOffset("started_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(began, ct);
    }
}

/// <summary>
/// Translates <c>channel.shield_mode.end</c> into <see cref="ShieldModeEndedEvent"/>. Payload fields:
/// <c>moderator_user_id</c>, <c>moderator_user_name</c>, <c>ended_at</c>.
/// </summary>
public sealed class ChannelShieldModeEndTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.shield_mode.end";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ShieldModeEndedEvent ended = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            ModeratorId = payload.GetRequiredString("moderator_user_id"),
            ModeratorDisplayName = payload.GetRequiredString("moderator_user_name"),
            EndedAt = payload.GetDateTimeOffset("ended_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(ended, ct);
    }
}
