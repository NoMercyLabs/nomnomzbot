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
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Shared payload-shape readers for the AutoMod translators. The v2 <c>automod.message.*</c> payloads carry the
/// message either as a plain <c>text</c> string or as a <c>message</c> object whose <c>fragments</c> each hold a
/// <c>text</c> piece; the event only needs the flattened plain text, so <see cref="ReadMessageText"/> prefers the
/// top-level <c>text</c> and otherwise concatenates the fragment texts. The <c>terms</c> reader maps the plain
/// string array, degrading to empty when absent. Every reader is null-tolerant so a translator stays on the hot
/// path and never faults the dispatcher over a missing field.
/// </summary>
internal static class AutoModPayload
{
    /// <summary>
    /// The flattened message text: the top-level <c>text</c> when present, otherwise the concatenation of the
    /// <c>message.fragments[].text</c> pieces (v2 shape), otherwise empty.
    /// </summary>
    public static string ReadMessageText(JsonElement payload)
    {
        string? topLevel = payload.GetString("text");
        if (!string.IsNullOrEmpty(topLevel))
        {
            return topLevel;
        }

        JsonElement? message = payload.GetObject("message");
        if (message is null)
        {
            return string.Empty;
        }

        string? messageText = message.Value.GetString("text");
        if (!string.IsNullOrEmpty(messageText))
        {
            return messageText;
        }

        if (
            !message.Value.TryGetProperty("fragments", out JsonElement fragments)
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

    /// <summary>Maps the <c>terms</c> string array; empty when absent / not an array.</summary>
    public static IReadOnlyList<string> ReadTerms(JsonElement payload)
    {
        if (
            !payload.TryGetProperty("terms", out JsonElement terms)
            || terms.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<string> result = new(terms.GetArrayLength());
        foreach (JsonElement term in terms.EnumerateArray())
        {
            if (term.ValueKind == JsonValueKind.String)
            {
                result.Add(term.GetString() ?? string.Empty);
            }
        }

        return result;
    }

    /// <summary>The nullable <c>overall_level</c>: an integer, or <c>null</c> when Twitch sends JSON null / omits it.</summary>
    public static int? ReadOverallLevel(JsonElement payload) =>
        payload.TryGetProperty("overall_level", out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int parsed)
            ? parsed
            : null;
}

/// <summary>
/// Translates <c>automod.message.hold</c> (v2) into <see cref="AutoModMessageHeldEvent"/>: a message was held by
/// AutoMod for review. The v2 payload nests the text under <c>message.fragments</c> and the offending classifier
/// under <c>automod.category</c> / <c>automod.level</c>.
/// </summary>
public sealed class AutoModMessageHoldTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "automod.message.hold";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        JsonElement automod = payload.GetObject("automod") ?? payload;
        AutoModMessageHeldEvent held = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            MessageId = payload.GetRequiredString("message_id"),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            Text = AutoModPayload.ReadMessageText(payload),
            Category = automod.GetRequiredString("category"),
            Level = automod.GetInt("level"),
            HeldAt = payload.GetDateTimeOffset("held_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(held, ct);
    }
}

/// <summary>
/// Translates <c>automod.message.update</c> (v2) into <see cref="AutoModMessageUpdatedEvent"/>: a moderator
/// resolved a held message. <c>status</c> is the raw Twitch verdict (<c>approved</c>/<c>denied</c>/<c>expired</c>).
/// </summary>
public sealed class AutoModMessageUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "automod.message.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        AutoModMessageUpdatedEvent updated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            MessageId = payload.GetRequiredString("message_id"),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            ModeratorId = payload.GetRequiredString("moderator_user_id"),
            ModeratorDisplayName = payload.GetRequiredString("moderator_user_name"),
            Status = payload.GetRequiredString("status"),
        };

        return PublishAsync(updated, ct);
    }
}

/// <summary>
/// Translates <c>automod.settings.update</c> into <see cref="AutoModSettingsUpdatedEvent"/>: the channel's AutoMod
/// sensitivity changed. <c>overall_level</c> is null when the broadcaster uses per-category levels. Reads the
/// fields off the flat event, transparently unwrapping a <c>data[0]</c> wrapper if Twitch nests the settings there.
/// </summary>
public sealed class AutoModSettingsUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "automod.settings.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = Unwrap(notification.Event);
        AutoModSettingsUpdatedEvent settings = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            ModeratorId = payload.GetRequiredString("moderator_user_id"),
            ModeratorDisplayName = payload.GetRequiredString("moderator_user_name"),
            OverallLevel = AutoModPayload.ReadOverallLevel(payload),
            Bullying = payload.GetInt("bullying"),
            Aggression = payload.GetInt("aggression"),
            Sexuality = payload.GetInt("sexuality_sex_or_gender"),
            Disability = payload.GetInt("disability"),
            Misogyny = payload.GetInt("misogyny"),
            RaceEthnicityOrReligion = payload.GetInt("race_ethnicity_or_religion"),
            SexBasedTerms = payload.GetInt("sex_based_terms"),
            Swearing = payload.GetInt("swearing"),
        };

        return PublishAsync(settings, ct);
    }

    /// <summary>Returns the first <c>data</c> element if the payload wraps the settings in a <c>data</c> array, else the payload itself.</summary>
    private static JsonElement Unwrap(JsonElement payload)
    {
        if (
            payload.TryGetProperty("data", out JsonElement data)
            && data.ValueKind == JsonValueKind.Array
            && data.GetArrayLength() > 0
        )
        {
            return data[0];
        }

        return payload;
    }
}

/// <summary>
/// Translates <c>automod.terms.update</c> into <see cref="AutoModTermsUpdatedEvent"/>: a permitted/blocked term
/// list changed. <c>action</c> is the raw Twitch action
/// (<c>add_permitted</c>/<c>remove_permitted</c>/<c>add_blocked</c>/<c>remove_blocked</c>).
/// </summary>
public sealed class AutoModTermsUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "automod.terms.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        AutoModTermsUpdatedEvent terms = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            ModeratorId = payload.GetRequiredString("moderator_user_id"),
            ModeratorDisplayName = payload.GetRequiredString("moderator_user_name"),
            Action = payload.GetRequiredString("action"),
            FromAutomod = payload.GetBool("from_automod"),
            Terms = AutoModPayload.ReadTerms(payload),
        };

        return PublishAsync(terms, ct);
    }
}
