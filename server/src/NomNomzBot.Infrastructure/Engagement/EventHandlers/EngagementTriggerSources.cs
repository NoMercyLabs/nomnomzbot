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
using NomNomzBot.Domain.Engagement.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Engagement.EventHandlers;

/// <summary>
/// Fires the streamer's bound response for a first-time chatter (engagement.md §4, trigger kind
/// <c>engagement.first_time_chatter</c>). Reuses the <see cref="TwitchAlertHandlerBase{T}"/> dispatch path,
/// so a configured <c>EventResponse</c> (chat message or pipeline) runs with the engagement vars.
/// </summary>
public sealed class FirstTimeChatterTriggerSource
    : TwitchAlertHandlerBase<FirstTimeChatterDetectedEvent>,
        IEventHandler<FirstTimeChatterDetectedEvent>
{
    protected override string EventTypeKey => "engagement.first_time_chatter";

    public FirstTimeChatterTriggerSource(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<FirstTimeChatterTriggerSource> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(FirstTimeChatterDetectedEvent e) => e.ViewerExternalUserId;

    protected override string? GetUserDisplayName(FirstTimeChatterDetectedEvent e) =>
        e.ViewerDisplayName;

    protected override Dictionary<string, string> BuildVariables(FirstTimeChatterDetectedEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.ViewerDisplayName,
            ["user.id"] = e.ViewerExternalUserId,
            ["viewer.name"] = e.ViewerDisplayName,
        };

    public Task HandleAsync(FirstTimeChatterDetectedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>
/// Fires the bound response for a returning chatter's first message this stream (engagement.md §4, trigger
/// kind <c>engagement.returning_chatter</c>). Exposes <c>{engagement.daysSinceLastSeen}</c>.
/// </summary>
public sealed class ReturningChatterTriggerSource
    : TwitchAlertHandlerBase<ReturningChatterDetectedEvent>,
        IEventHandler<ReturningChatterDetectedEvent>
{
    protected override string EventTypeKey => "engagement.returning_chatter";

    public ReturningChatterTriggerSource(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<ReturningChatterTriggerSource> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(ReturningChatterDetectedEvent e) => e.ViewerExternalUserId;

    protected override string? GetUserDisplayName(ReturningChatterDetectedEvent e) =>
        e.ViewerDisplayName;

    protected override Dictionary<string, string> BuildVariables(ReturningChatterDetectedEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.ViewerDisplayName,
            ["user.id"] = e.ViewerExternalUserId,
            ["viewer.name"] = e.ViewerDisplayName,
            ["engagement.daysSinceLastSeen"] = e.DaysSinceLastSeen.ToString(),
        };

    public Task HandleAsync(ReturningChatterDetectedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>
/// Fires the bound response for a watch-streak milestone (engagement.md §4, trigger kind
/// <c>engagement.watch_streak</c>). Exposes <c>{engagement.streak}</c>.
/// </summary>
public sealed class WatchStreakTriggerSource
    : TwitchAlertHandlerBase<WatchStreakMilestoneEvent>,
        IEventHandler<WatchStreakMilestoneEvent>
{
    protected override string EventTypeKey => "engagement.watch_streak";

    public WatchStreakTriggerSource(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<WatchStreakTriggerSource> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(WatchStreakMilestoneEvent e) => e.ViewerExternalUserId;

    protected override string? GetUserDisplayName(WatchStreakMilestoneEvent e) =>
        e.ViewerDisplayName;

    protected override Dictionary<string, string> BuildVariables(WatchStreakMilestoneEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.ViewerDisplayName,
            ["user.id"] = e.ViewerExternalUserId,
            ["viewer.name"] = e.ViewerDisplayName,
            ["engagement.streak"] = e.StreakCount.ToString(),
        };

    public Task HandleAsync(WatchStreakMilestoneEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
