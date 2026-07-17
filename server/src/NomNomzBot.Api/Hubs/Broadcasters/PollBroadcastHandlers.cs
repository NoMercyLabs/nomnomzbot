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
using NomNomzBot.Application.Abstractions.Persistence;
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

/// <summary>Broadcasts a poll opening (<c>channel.poll.begin</c>) to dashboard clients AND, identically, to
/// overlay widgets + the feed (the <c>poll_prediction</c> widget binds <c>poll_begin</c>).</summary>
public sealed class PollBeganBroadcastHandler : IEventHandler<PollBeganEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public PollBeganBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(PollBeganEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        PollBeganAlertDto dto = new(
            @event.PollId,
            @event.Title,
            PollAlertMapper.MapChoices(@event.Choices),
            @event.DurationSeconds,
            @event.EndsAt
        );

        await _notifier.NotifyChannelAsync(@event.BroadcasterId.ToString(), "poll_begin", dto, ct);

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "poll_begin",
            dto,
            ct
        );
    }
}

/// <summary>Broadcasts a running poll's vote tallies (<c>channel.poll.progress</c>) to dashboard clients AND,
/// identically, to overlay widgets + the feed.</summary>
public sealed class PollProgressBroadcastHandler : IEventHandler<PollProgressEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public PollProgressBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(PollProgressEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        PollProgressAlertDto dto = new(
            @event.PollId,
            @event.Title,
            PollAlertMapper.MapChoices(@event.Choices),
            @event.EndsAt
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "poll_progress",
            dto,
            ct
        );

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "poll_progress",
            dto,
            ct
        );
    }
}

/// <summary>Broadcasts a poll's terminal result (<c>channel.poll.end</c>) to dashboard clients AND, identically,
/// to overlay widgets + the feed.</summary>
public sealed class PollEndedBroadcastHandler : IEventHandler<PollEndedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _widgets;

    public PollEndedBroadcastHandler(
        IDashboardNotifier notifier,
        IApplicationDbContext db,
        IWidgetNotifier widgets
    )
    {
        _notifier = notifier;
        _db = db;
        _widgets = widgets;
    }

    public async Task HandleAsync(PollEndedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        PollEndedAlertDto dto = new(
            @event.PollId,
            @event.Title,
            @event.Status,
            PollAlertMapper.MapChoices(@event.Choices),
            @event.WinningChoiceId
        );

        await _notifier.NotifyChannelAsync(@event.BroadcasterId.ToString(), "poll_end", dto, ct);

        await OverlayAlertBroadcast.ToOverlaysAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "poll_end",
            dto,
            ct
        );
    }
}
