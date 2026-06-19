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
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Community.EventHandlers;

/// <summary>Handles hype train begin events.</summary>
public sealed class HypeTrainBeganHandler
    : TwitchAlertHandlerBase<HypeTrainBeganEvent>,
        IEventHandler<HypeTrainBeganEvent>
{
    protected override string EventTypeKey => "hype_train_begin";

    public HypeTrainBeganHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<HypeTrainBeganHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(HypeTrainBeganEvent e) => e.BroadcasterId;

    protected override string? GetUserDisplayName(HypeTrainBeganEvent e) => null;

    protected override Dictionary<string, string> BuildVariables(HypeTrainBeganEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["hype_train.id"] = e.HypeTrainId,
            ["hype_train.level"] = e.Level.ToString(),
            ["hype_train.total"] = e.Total.ToString(),
            ["hype_train.goal"] = e.Goal.ToString(),
        };

    public Task HandleAsync(HypeTrainBeganEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>Handles hype train ended events.</summary>
public sealed class HypeTrainEndedHandler
    : TwitchAlertHandlerBase<HypeTrainEndedEvent>,
        IEventHandler<HypeTrainEndedEvent>
{
    protected override string EventTypeKey => "hype_train_end";

    public HypeTrainEndedHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<HypeTrainEndedHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(HypeTrainEndedEvent e) => e.BroadcasterId;

    protected override string? GetUserDisplayName(HypeTrainEndedEvent e) => null;

    protected override Dictionary<string, string> BuildVariables(HypeTrainEndedEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["hype_train.id"] = e.HypeTrainId,
            ["hype_train.level"] = e.Level.ToString(),
            ["hype_train.total"] = e.Total.ToString(),
        };

    public Task HandleAsync(HypeTrainEndedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
