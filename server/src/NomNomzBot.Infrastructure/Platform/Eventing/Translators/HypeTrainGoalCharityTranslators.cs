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
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Shared payload-shape readers for the hype-train (v2) and charity translators: the hype-train
/// <c>top_contributions</c> array and the charity <c>amount</c> / <c>current_amount</c> / <c>target_amount</c>
/// money objects. A money amount is read as Twitch sends it — integer minor units, decimal places, currency code,
/// never pre-divided — so the tuple is carried verbatim into the typed event.
/// </summary>
internal static class HypeCharityPayload
{
    /// <summary>Maps the hype-train <c>top_contributions</c> array; empty when absent.</summary>
    public static IReadOnlyList<HypeTrainContribution> ReadTopContributions(JsonElement payload)
    {
        if (
            !payload.TryGetProperty("top_contributions", out JsonElement contributions)
            || contributions.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<HypeTrainContribution> result = new(contributions.GetArrayLength());
        foreach (JsonElement contribution in contributions.EnumerateArray())
        {
            result.Add(
                new HypeTrainContribution(
                    contribution.GetRequiredString("user_id"),
                    contribution.GetRequiredString("user_login"),
                    contribution.GetRequiredString("user_name"),
                    contribution.GetRequiredString("type"),
                    contribution.GetInt("total")
                )
            );
        }

        return result;
    }

    /// <summary>Reads a Twitch money object (<c>value</c>, <c>decimal_places</c>, <c>currency</c>) at <paramref name="name"/>.</summary>
    public static Money ReadMoney(JsonElement payload, string name)
    {
        JsonElement? amount = payload.GetObject(name);
        return amount is null
            ? new Money(0, 0, string.Empty)
            : new Money(
                amount.Value.GetInt("value"),
                amount.Value.GetInt("decimal_places"),
                amount.Value.GetRequiredString("currency")
            );
    }
}

/// <summary>A Twitch money amount kept in its raw minor-unit form (never pre-divided).</summary>
internal readonly record struct Money(int Value, int DecimalPlaces, string Currency);

/// <summary>Translates <c>channel.hype_train.begin</c> (v2) into <see cref="HypeTrainBeganEvent"/>.</summary>
public sealed class ChannelHypeTrainBeginTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.hype_train.begin";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        HypeTrainBeganEvent began = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            HypeTrainId = payload.GetRequiredString("id"),
            Level = payload.GetInt("level"),
            Total = payload.GetInt("total"),
            Progress = payload.GetInt("progress"),
            Goal = payload.GetInt("goal"),
            TopContributions = HypeCharityPayload.ReadTopContributions(payload),
            ExpiresAt = payload.GetDateTimeOffset("expires_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(began, ct);
    }
}

/// <summary>Translates <c>channel.hype_train.progress</c> (v2) into <see cref="HypeTrainProgressEvent"/>.</summary>
public sealed class ChannelHypeTrainProgressTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.hype_train.progress";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        HypeTrainProgressEvent progress = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            HypeTrainId = payload.GetRequiredString("id"),
            Level = payload.GetInt("level"),
            Total = payload.GetInt("total"),
            Progress = payload.GetInt("progress"),
            Goal = payload.GetInt("goal"),
            TopContributions = HypeCharityPayload.ReadTopContributions(payload),
            ExpiresAt = payload.GetDateTimeOffset("expires_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(progress, ct);
    }
}

/// <summary>Translates <c>channel.hype_train.end</c> (v2) into <see cref="HypeTrainEndedEvent"/>.</summary>
public sealed class ChannelHypeTrainEndTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.hype_train.end";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        HypeTrainEndedEvent ended = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            HypeTrainId = payload.GetRequiredString("id"),
            Level = payload.GetInt("level"),
            Total = payload.GetInt("total"),
            TopContributions = HypeCharityPayload.ReadTopContributions(payload),
            EndedAt = payload.GetDateTimeOffset("ended_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(ended, ct);
    }
}

/// <summary>Translates <c>channel.goal.begin</c> into <see cref="GoalBeganEvent"/>.</summary>
public sealed class ChannelGoalBeginTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.goal.begin";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        GoalBeganEvent began = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            GoalId = payload.GetRequiredString("id"),
            Type = payload.GetRequiredString("type"),
            Description = payload.GetRequiredString("description"),
            CurrentAmount = payload.GetInt("current_amount"),
            TargetAmount = payload.GetInt("target_amount"),
            StartedAt = payload.GetDateTimeOffset("started_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(began, ct);
    }
}

/// <summary>Translates <c>channel.goal.progress</c> into <see cref="GoalProgressEvent"/> (updated current amount).</summary>
public sealed class ChannelGoalProgressTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.goal.progress";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        GoalProgressEvent progress = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            GoalId = payload.GetRequiredString("id"),
            Type = payload.GetRequiredString("type"),
            Description = payload.GetRequiredString("description"),
            CurrentAmount = payload.GetInt("current_amount"),
            TargetAmount = payload.GetInt("target_amount"),
            StartedAt = payload.GetDateTimeOffset("started_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(progress, ct);
    }
}

/// <summary>Translates <c>channel.goal.end</c> into <see cref="GoalEndedEvent"/> (with achievement + end time).</summary>
public sealed class ChannelGoalEndTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.goal.end";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        GoalEndedEvent ended = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            GoalId = payload.GetRequiredString("id"),
            Type = payload.GetRequiredString("type"),
            Description = payload.GetRequiredString("description"),
            CurrentAmount = payload.GetInt("current_amount"),
            TargetAmount = payload.GetInt("target_amount"),
            IsAchieved = payload.GetBool("is_achieved"),
            StartedAt = payload.GetDateTimeOffset("started_at") ?? Clock.GetUtcNow(),
            EndedAt = payload.GetDateTimeOffset("ended_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(ended, ct);
    }
}

/// <summary>Translates <c>channel.charity_campaign.start</c> into <see cref="CharityCampaignStartedEvent"/>.</summary>
public sealed class ChannelCharityCampaignStartTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.charity_campaign.start";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        Money current = HypeCharityPayload.ReadMoney(payload, "current_amount");
        Money target = HypeCharityPayload.ReadMoney(payload, "target_amount");
        CharityCampaignStartedEvent started = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            CampaignId = payload.GetRequiredString("id"),
            CharityName = payload.GetRequiredString("charity_name"),
            Description = payload.GetString("charity_description"),
            CurrentAmountValue = current.Value,
            CurrentAmountDecimalPlaces = current.DecimalPlaces,
            CurrentAmountCurrency = current.Currency,
            TargetAmountValue = target.Value,
            TargetAmountDecimalPlaces = target.DecimalPlaces,
            TargetAmountCurrency = target.Currency,
            StartedAt = payload.GetDateTimeOffset("started_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(started, ct);
    }
}

/// <summary>Translates <c>channel.charity_campaign.progress</c> into <see cref="CharityCampaignProgressEvent"/>.</summary>
public sealed class ChannelCharityCampaignProgressTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.charity_campaign.progress";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        Money current = HypeCharityPayload.ReadMoney(payload, "current_amount");
        Money target = HypeCharityPayload.ReadMoney(payload, "target_amount");
        CharityCampaignProgressEvent progress = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            CampaignId = payload.GetRequiredString("id"),
            CharityName = payload.GetRequiredString("charity_name"),
            Description = payload.GetString("charity_description"),
            CurrentAmountValue = current.Value,
            CurrentAmountDecimalPlaces = current.DecimalPlaces,
            CurrentAmountCurrency = current.Currency,
            TargetAmountValue = target.Value,
            TargetAmountDecimalPlaces = target.DecimalPlaces,
            TargetAmountCurrency = target.Currency,
        };

        return PublishAsync(progress, ct);
    }
}

/// <summary>Translates <c>channel.charity_campaign.donate</c> into <see cref="CharityDonationEvent"/>.</summary>
public sealed class ChannelCharityCampaignDonateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.charity_campaign.donate";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        Money amount = HypeCharityPayload.ReadMoney(payload, "amount");
        CharityDonationEvent donation = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            CampaignId = payload.GetRequiredString("campaign_id"),
            CharityName = payload.GetRequiredString("charity_name"),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            AmountValue = amount.Value,
            AmountDecimalPlaces = amount.DecimalPlaces,
            AmountCurrency = amount.Currency,
        };

        return PublishAsync(donation, ct);
    }
}

/// <summary>Translates <c>channel.charity_campaign.stop</c> into <see cref="CharityCampaignStoppedEvent"/>.</summary>
public sealed class ChannelCharityCampaignStopTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.charity_campaign.stop";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        Money current = HypeCharityPayload.ReadMoney(payload, "current_amount");
        Money target = HypeCharityPayload.ReadMoney(payload, "target_amount");
        CharityCampaignStoppedEvent stopped = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            CampaignId = payload.GetRequiredString("id"),
            CharityName = payload.GetRequiredString("charity_name"),
            Description = payload.GetString("charity_description"),
            CurrentAmountValue = current.Value,
            CurrentAmountDecimalPlaces = current.DecimalPlaces,
            CurrentAmountCurrency = current.Currency,
            TargetAmountValue = target.Value,
            TargetAmountDecimalPlaces = target.DecimalPlaces,
            TargetAmountCurrency = target.Currency,
            StoppedAt = payload.GetDateTimeOffset("stopped_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(stopped, ct);
    }
}
