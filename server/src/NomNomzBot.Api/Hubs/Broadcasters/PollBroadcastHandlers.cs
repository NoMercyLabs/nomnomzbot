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

/// <summary>Maps the shared poll-choice shape onto its hub DTO — reused by all three poll broadcasters below.</summary>
internal static class PollAlertMapper
{
    public static IReadOnlyList<PollChoiceDto> MapChoices(IReadOnlyList<PollChoice> choices) =>
        choices
            .Select(c => new PollChoiceDto(c.Id, c.Title, c.Votes, c.ChannelPointsVotes))
            .ToList();
}

/// <summary>Broadcasts a poll opening (<c>channel.poll.begin</c>) to dashboard clients.</summary>
public sealed class PollBeganBroadcastHandler : IEventHandler<PollBeganEvent>
{
    private readonly IDashboardNotifier _notifier;

    public PollBeganBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(PollBeganEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "poll_begin",
            new PollBeganAlertDto(
                @event.PollId,
                @event.Title,
                PollAlertMapper.MapChoices(@event.Choices),
                @event.DurationSeconds,
                @event.EndsAt
            ),
            ct
        );
    }
}

/// <summary>Broadcasts a running poll's vote tallies (<c>channel.poll.progress</c>) to dashboard clients.</summary>
public sealed class PollProgressBroadcastHandler : IEventHandler<PollProgressEvent>
{
    private readonly IDashboardNotifier _notifier;

    public PollProgressBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(PollProgressEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "poll_progress",
            new PollProgressAlertDto(
                @event.PollId,
                @event.Title,
                PollAlertMapper.MapChoices(@event.Choices),
                @event.EndsAt
            ),
            ct
        );
    }
}

/// <summary>Broadcasts a poll's terminal result (<c>channel.poll.end</c>) to dashboard clients.</summary>
public sealed class PollEndedBroadcastHandler : IEventHandler<PollEndedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public PollEndedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(PollEndedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "poll_end",
            new PollEndedAlertDto(
                @event.PollId,
                @event.Title,
                @event.Status,
                PollAlertMapper.MapChoices(@event.Choices),
                @event.WinningChoiceId
            ),
            ct
        );
    }
}
