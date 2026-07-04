// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Maps the shared hype train contribution shape onto its hub DTO — reused by the dashboard broadcasters below.
/// The overlay-facing siblings in <c>WidgetAlertHandlers.cs</c> forward a lighter anonymous payload instead,
/// matching the existing overlay alert convention (spec: overlay widgets consume flattened fields, not DTOs).
/// </summary>
internal static class HypeTrainAlertMapper
{
    public static IReadOnlyList<HypeTrainContributionDto> MapContributions(
        IReadOnlyList<HypeTrainContribution> contributions
    ) =>
        contributions
            .Select(c => new HypeTrainContributionDto(
                c.UserId,
                c.UserLogin,
                c.UserDisplayName,
                c.Type,
                c.Total
            ))
            .ToList();
}

/// <summary>Broadcasts a hype train starting (<c>channel.hype_train.begin</c>) to dashboard clients.</summary>
public sealed class HypeTrainBeganBroadcastHandler : IEventHandler<HypeTrainBeganEvent>
{
    private readonly IDashboardNotifier _notifier;

    public HypeTrainBeganBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(HypeTrainBeganEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "hype_train_begin",
            new HypeTrainBeganAlertDto(
                @event.HypeTrainId,
                @event.Level,
                @event.Total,
                @event.Progress,
                @event.Goal,
                HypeTrainAlertMapper.MapContributions(@event.TopContributions),
                @event.ExpiresAt
            ),
            ct
        );
    }
}

/// <summary>Broadcasts a hype train progress tick (<c>channel.hype_train.progress</c>) to dashboard clients.</summary>
public sealed class HypeTrainProgressBroadcastHandler : IEventHandler<HypeTrainProgressEvent>
{
    private readonly IDashboardNotifier _notifier;

    public HypeTrainProgressBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(HypeTrainProgressEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "hype_train_progress",
            new HypeTrainProgressAlertDto(
                @event.HypeTrainId,
                @event.Level,
                @event.Total,
                @event.Progress,
                @event.Goal,
                HypeTrainAlertMapper.MapContributions(@event.TopContributions),
                @event.ExpiresAt
            ),
            ct
        );
    }
}

/// <summary>Broadcasts a hype train's final level (<c>channel.hype_train.end</c>) to dashboard clients.</summary>
public sealed class HypeTrainEndedBroadcastHandler : IEventHandler<HypeTrainEndedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public HypeTrainEndedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(HypeTrainEndedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "hype_train_end",
            new HypeTrainEndedAlertDto(
                @event.HypeTrainId,
                @event.Level,
                @event.Total,
                HypeTrainAlertMapper.MapContributions(@event.TopContributions),
                @event.EndedAt
            ),
            ct
        );
    }
}
