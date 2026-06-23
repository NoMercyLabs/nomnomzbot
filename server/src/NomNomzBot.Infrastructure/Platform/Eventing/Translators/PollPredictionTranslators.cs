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
/// Shared payload-shape readers for the poll and prediction translators: the <c>choices</c> and <c>outcomes</c>
/// arrays both nest per-option vote/pool counts, and both families need the same "id of the option with the most
/// votes" winner derivation for their end event. Kept here so the begin / progress / end translators of each
/// family read those nested shapes identically.
/// </summary>
internal static class PollPredictionPayload
{
    /// <summary>Maps the <c>choices</c> array (poll) into <see cref="PollChoice"/> records; empty when absent.</summary>
    public static IReadOnlyList<PollChoice> ReadPollChoices(JsonElement payload)
    {
        if (
            !payload.TryGetProperty("choices", out JsonElement choices)
            || choices.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<PollChoice> result = new(choices.GetArrayLength());
        foreach (JsonElement choice in choices.EnumerateArray())
        {
            result.Add(
                new PollChoice(
                    choice.GetRequiredString("id"),
                    choice.GetRequiredString("title"),
                    choice.GetInt("votes"),
                    choice.GetInt("channel_points_votes")
                )
            );
        }

        return result;
    }

    /// <summary>The id of the choice with the most total votes, or <c>null</c> when there were no votes.</summary>
    public static string? WinningPollChoiceId(IReadOnlyList<PollChoice> choices)
    {
        PollChoice? leader = null;
        foreach (PollChoice choice in choices)
        {
            if (choice.Votes > 0 && (leader is null || choice.Votes > leader.Votes))
            {
                leader = choice;
            }
        }

        return leader?.Id;
    }

    /// <summary>Maps the <c>outcomes</c> array (prediction) into <see cref="PredictionOutcome"/> records.</summary>
    public static IReadOnlyList<PredictionOutcome> ReadPredictionOutcomes(JsonElement payload)
    {
        if (
            !payload.TryGetProperty("outcomes", out JsonElement outcomes)
            || outcomes.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<PredictionOutcome> result = new(outcomes.GetArrayLength());
        foreach (JsonElement outcome in outcomes.EnumerateArray())
        {
            result.Add(
                new PredictionOutcome(
                    outcome.GetRequiredString("id"),
                    outcome.GetRequiredString("title"),
                    outcome.GetInt("channel_points"),
                    outcome.GetInt("users"),
                    outcome.GetRequiredString("color")
                )
            );
        }

        return result;
    }
}

/// <summary>Translates <c>channel.poll.begin</c> into <see cref="PollBeganEvent"/>.</summary>
public sealed class ChannelPollBeginTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.poll.begin";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        PollBeganEvent began = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            PollId = payload.GetRequiredString("id"),
            Title = payload.GetRequiredString("title"),
            Choices = PollPredictionPayload.ReadPollChoices(payload),
            DurationSeconds = DurationSeconds(payload),
            EndsAt = payload.GetDateTimeOffset("ends_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(began, ct);
    }

    private DateTimeOffset StartedAt(JsonElement payload) =>
        payload.GetDateTimeOffset("started_at") ?? Clock.GetUtcNow();

    private int DurationSeconds(JsonElement payload)
    {
        DateTimeOffset? endsAt = payload.GetDateTimeOffset("ends_at");
        return endsAt is null
            ? 0
            : (int)Math.Max(0, (endsAt.Value - StartedAt(payload)).TotalSeconds);
    }
}

/// <summary>Translates <c>channel.poll.progress</c> into <see cref="PollProgressEvent"/> (running tallies).</summary>
public sealed class ChannelPollProgressTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.poll.progress";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        PollProgressEvent progress = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            PollId = payload.GetRequiredString("id"),
            Title = payload.GetRequiredString("title"),
            Choices = PollPredictionPayload.ReadPollChoices(payload),
            EndsAt = payload.GetDateTimeOffset("ends_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(progress, ct);
    }
}

/// <summary>
/// Translates <c>channel.poll.end</c> into <see cref="PollEndedEvent"/>. Carries the terminal
/// <c>status</c> (completed / archived / terminated) and the derived winning choice.
/// </summary>
public sealed class ChannelPollEndTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.poll.end";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        IReadOnlyList<PollChoice> choices = PollPredictionPayload.ReadPollChoices(payload);
        PollEndedEvent ended = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            PollId = payload.GetRequiredString("id"),
            Title = payload.GetRequiredString("title"),
            Status = payload.GetRequiredString("status"),
            Choices = choices,
            WinningChoiceId = PollPredictionPayload.WinningPollChoiceId(choices),
        };

        return PublishAsync(ended, ct);
    }
}

/// <summary>Translates <c>channel.prediction.begin</c> into <see cref="PredictionBeganEvent"/>.</summary>
public sealed class ChannelPredictionBeginTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.prediction.begin";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        DateTimeOffset startedAt = payload.GetDateTimeOffset("started_at") ?? Clock.GetUtcNow();
        DateTimeOffset locksAt = payload.GetDateTimeOffset("locks_at") ?? Clock.GetUtcNow();
        PredictionBeganEvent began = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            PredictionId = payload.GetRequiredString("id"),
            Title = payload.GetRequiredString("title"),
            Outcomes = PollPredictionPayload.ReadPredictionOutcomes(payload),
            WindowSeconds = (int)Math.Max(0, (locksAt - startedAt).TotalSeconds),
            LocksAt = locksAt,
        };

        return PublishAsync(began, ct);
    }
}

/// <summary>Translates <c>channel.prediction.progress</c> into <see cref="PredictionProgressEvent"/>.</summary>
public sealed class ChannelPredictionProgressTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.prediction.progress";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        PredictionProgressEvent progress = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            PredictionId = payload.GetRequiredString("id"),
            Title = payload.GetRequiredString("title"),
            Outcomes = PollPredictionPayload.ReadPredictionOutcomes(payload),
            LocksAt = payload.GetDateTimeOffset("locks_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(progress, ct);
    }
}

/// <summary>Translates <c>channel.prediction.lock</c> into <see cref="PredictionLockedEvent"/>.</summary>
public sealed class ChannelPredictionLockTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.prediction.lock";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        PredictionLockedEvent locked = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            PredictionId = payload.GetRequiredString("id"),
            Title = payload.GetRequiredString("title"),
            Outcomes = PollPredictionPayload.ReadPredictionOutcomes(payload),
        };

        return PublishAsync(locked, ct);
    }
}

/// <summary>
/// Translates <c>channel.prediction.end</c> into <see cref="PredictionEndedEvent"/>. Carries the terminal
/// <c>status</c> (resolved / canceled) and the <c>winning_outcome_id</c> (null on cancel).
/// </summary>
public sealed class ChannelPredictionEndTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.prediction.end";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        PredictionEndedEvent ended = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            PredictionId = payload.GetRequiredString("id"),
            Title = payload.GetRequiredString("title"),
            Status = payload.GetRequiredString("status"),
            Outcomes = PollPredictionPayload.ReadPredictionOutcomes(payload),
            WinningOutcomeId = payload.GetString("winning_outcome_id"),
        };

        return PublishAsync(ended, ct);
    }
}
