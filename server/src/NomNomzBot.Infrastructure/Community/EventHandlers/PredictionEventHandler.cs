// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Community.EventHandlers;

/// <summary>Handles prediction begin events.</summary>
public sealed class PredictionBeganHandler
    : TwitchAlertHandlerBase<PredictionBeganEvent>,
        IEventHandler<PredictionBeganEvent>
{
    protected override string EventTypeKey => "channel.prediction.begin";

    public PredictionBeganHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<PredictionBeganHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(PredictionBeganEvent e) => e.BroadcasterId.ToString();

    protected override string? GetUserDisplayName(PredictionBeganEvent e) => null;

    protected override Dictionary<string, string> BuildVariables(PredictionBeganEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["prediction.id"] = e.PredictionId,
            ["prediction.title"] = e.Title,
            ["prediction.window"] = e.WindowSeconds.ToString(),
            ["prediction.outcomes"] = string.Join(", ", e.Outcomes.Select(o => o.Title)),
        };

    protected override PredictionBeganEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            PredictionId = "bc637af0-7766-4e41-9e2b-2f9c0a8e4a1f",
            Title = "Will I beat the boss?",
            Outcomes = [new("o1", "Yes", 0, 0, "blue"), new("o2", "No", 0, 0, "pink")],
            WindowSeconds = 120,
            LocksAt = now.AddMinutes(2),
        };

    public Task HandleAsync(PredictionBeganEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>Handles prediction locked events.</summary>
public sealed class PredictionLockedHandler
    : TwitchAlertHandlerBase<PredictionLockedEvent>,
        IEventHandler<PredictionLockedEvent>
{
    protected override string EventTypeKey => "channel.prediction.lock";

    public PredictionLockedHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<PredictionLockedHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(PredictionLockedEvent e) => e.BroadcasterId.ToString();

    protected override string? GetUserDisplayName(PredictionLockedEvent e) => null;

    protected override Dictionary<string, string> BuildVariables(PredictionLockedEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["prediction.id"] = e.PredictionId,
            ["prediction.title"] = e.Title,
            ["prediction.total_points"] = e.Outcomes.Sum(o => o.ChannelPoints).ToString(),
        };

    protected override PredictionLockedEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            PredictionId = "bc637af0-7766-4e41-9e2b-2f9c0a8e4a1f",
            Title = "Will I beat the boss?",
            Outcomes = [new("o1", "Yes", 5200, 18, "blue"), new("o2", "No", 2400, 9, "pink")],
        };

    public Task HandleAsync(PredictionLockedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>Handles prediction ended events.</summary>
public sealed class PredictionEndedHandler
    : TwitchAlertHandlerBase<PredictionEndedEvent>,
        IEventHandler<PredictionEndedEvent>
{
    protected override string EventTypeKey => "channel.prediction.end";

    public PredictionEndedHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<PredictionEndedHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(PredictionEndedEvent e) => e.BroadcasterId.ToString();

    protected override string? GetUserDisplayName(PredictionEndedEvent e) => null;

    protected override Dictionary<string, string> BuildVariables(PredictionEndedEvent e)
    {
        PredictionOutcome? winner = e.Outcomes.FirstOrDefault(o => o.Id == e.WinningOutcomeId);
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["prediction.id"] = e.PredictionId,
            ["prediction.title"] = e.Title,
            ["prediction.status"] = e.Status,
            ["prediction.winner"] = winner?.Title ?? string.Empty,
            ["prediction.winner.points"] = winner?.ChannelPoints.ToString() ?? "0",
            ["prediction.winner.users"] = winner?.Users.ToString() ?? "0",
        };
    }

    protected override PredictionEndedEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            PredictionId = "bc637af0-7766-4e41-9e2b-2f9c0a8e4a1f",
            Title = "Will I beat the boss?",
            Status = "resolved",
            Outcomes = [new("o1", "Yes", 5200, 18, "blue"), new("o2", "No", 2400, 9, "pink")],
            WinningOutcomeId = "o1",
        };

    public Task HandleAsync(PredictionEndedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
