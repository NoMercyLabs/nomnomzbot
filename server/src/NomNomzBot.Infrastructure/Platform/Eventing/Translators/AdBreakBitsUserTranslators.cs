// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Translates <c>channel.ad_break.begin</c> into <see cref="AdBreakBeganEvent"/>. Twitch has historically typed
/// <c>duration_seconds</c> and <c>is_automatic</c> as JSON strings ("180" / "true") while current payloads send a
/// number / boolean — both shapes are accepted here so a representation change never faults the translator. The
/// requester fields are absent for an automatic break.
/// </summary>
public sealed class ChannelAdBreakBeginTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.ad_break.begin";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        AdBreakBeganEvent began = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            DurationSeconds = ReadFlexibleInt(payload, "duration_seconds"),
            IsAutomatic = ReadFlexibleBool(payload, "is_automatic"),
            StartedAt = payload.GetDateTimeOffset("started_at") ?? Clock.GetUtcNow(),
            RequesterUserId = payload.GetString("requester_user_id"),
            RequesterDisplayName = payload.GetString("requester_user_name"),
        };

        return PublishAsync(began, ct);
    }

    /// <summary>Reads an int that Twitch may send as a JSON number or a numeric string; 0 when absent.</summary>
    private static int ReadFlexibleInt(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out JsonElement value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return
            value.ValueKind == JsonValueKind.String
            && int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed
            )
            ? parsed
            : 0;
    }

    /// <summary>Reads a bool that Twitch may send as a JSON boolean or "true"/"false" string; false when absent.</summary>
    private static bool ReadFlexibleBool(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return value.ValueKind == JsonValueKind.String
            && bool.TryParse(value.GetString(), out bool parsed)
            && parsed;
    }
}

/// <summary>
/// Translates <c>channel.bits.use</c> into <see cref="BitsUsedEvent"/> — the unified Bits event covering cheers
/// and Power-up redemptions. The accompanying chat text is nested under <c>message.text</c> (absent on a bare
/// Power-up), so it degrades to <c>null</c> rather than empty.
/// </summary>
public sealed class ChannelBitsUseTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.bits.use";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        BitsUsedEvent used = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            Bits = payload.GetInt("bits"),
            Type = payload.GetRequiredString("type"),
            MessageText = ReadMessageText(payload),
        };

        return PublishAsync(used, ct);
    }

    /// <summary>The text nested under the <c>message</c> object, or <c>null</c> when absent / empty.</summary>
    private static string? ReadMessageText(JsonElement payload)
    {
        JsonElement? message = payload.GetObject("message");
        if (message is null)
        {
            return null;
        }

        string? text = message.Value.GetString("text");
        return string.IsNullOrEmpty(text) ? null : text;
    }
}

/// <summary>
/// Translates <c>user.update</c> into <see cref="UserUpdatedEvent"/>. This is a user-scoped notification — the
/// tenant is the user — and <c>email</c> is present only when the subscription holds <c>user:read:email</c>, so
/// it degrades to <c>null</c>.
/// </summary>
public sealed class UserUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "user.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        UserUpdatedEvent updated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserLogin = payload.GetRequiredString("user_login"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            Email = payload.GetString("email"),
            Description = payload.GetRequiredString("description"),
        };

        return PublishAsync(updated, ct);
    }
}

/// <summary>
/// Translates <c>user.whisper.message</c> into <see cref="WhisperReceivedEvent"/>. The body is nested under the
/// <c>whisper</c> object's <c>text</c> field. The recipient (<c>to_user_id</c>) is the bot's own account: when
/// that account is itself a channel (single-account self-host) the tenant is that channel, otherwise the sink
/// attributed the notification to the platform sentinel (<c>Guid.Empty</c>) — a whisper names no channel.
/// </summary>
public sealed class UserWhisperMessageTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "user.whisper.message";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        WhisperReceivedEvent received = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            WhisperId = payload.GetRequiredString("whisper_id"),
            FromUserId = payload.GetRequiredString("from_user_id"),
            FromUserDisplayName = payload.GetRequiredString("from_user_name"),
            FromUserLogin = payload.GetRequiredString("from_user_login"),
            ToUserId = payload.GetRequiredString("to_user_id"),
            Text = payload.GetObject("whisper")?.GetRequiredString("text") ?? string.Empty,
        };

        return PublishAsync(received, ct);
    }
}
