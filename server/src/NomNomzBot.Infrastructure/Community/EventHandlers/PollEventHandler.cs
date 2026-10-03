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

/// <summary>
/// Handles poll lifecycle events.
/// Executes the event_response:poll_begin / poll_end pipeline if configured.
/// </summary>
public sealed class PollBeganHandler
    : TwitchAlertHandlerBase<PollBeganEvent>,
        IEventHandler<PollBeganEvent>
{
    protected override string EventTypeKey => "channel.poll.begin";

    public PollBeganHandler(IServiceScopeFactory s, IPipelineEngine p, ILogger<PollBeganHandler> l)
        : base(s, p, l) { }

    protected override string? GetUserId(PollBeganEvent e) => e.BroadcasterId.ToString();

    protected override string? GetUserDisplayName(PollBeganEvent e) => null;

    protected override Dictionary<string, string> BuildVariables(PollBeganEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["poll.id"] = e.PollId,
            ["poll.title"] = e.Title,
            ["poll.duration"] = HumanDuration(e.DurationSeconds),
            ["poll.choices"] = string.Join(", ", e.Choices.Select(c => c.Title)),
        };

    protected override PollBeganEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            PollId = "ed961efd-8a3f-4cf5-a9d0-e616c590cd2a",
            Title = "Which game next?",
            Choices = [new("c1", "Elden Ring", 0, 0), new("c2", "Hades II", 0, 0)],
            DurationSeconds = 120,
            EndsAt = now.AddMinutes(2),
        };

    public Task HandleAsync(PollBeganEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>Handles poll ended events and optionally posts results to chat.</summary>
public sealed class PollEndedHandler
    : TwitchAlertHandlerBase<PollEndedEvent>,
        IEventHandler<PollEndedEvent>
{
    protected override string EventTypeKey => "channel.poll.end";

    public PollEndedHandler(IServiceScopeFactory s, IPipelineEngine p, ILogger<PollEndedHandler> l)
        : base(s, p, l) { }

    protected override string? GetUserId(PollEndedEvent e) => e.BroadcasterId.ToString();

    protected override string? GetUserDisplayName(PollEndedEvent e) => null;

    protected override Dictionary<string, string> BuildVariables(PollEndedEvent e)
    {
        PollChoice? winner = e.Choices.OrderByDescending(c => c.Votes).FirstOrDefault();
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["poll.id"] = e.PollId,
            ["poll.title"] = e.Title,
            ["poll.status"] = e.Status,
            ["poll.winner"] = winner?.Title ?? string.Empty,
            ["poll.winner.votes"] = winner?.Votes.ToString() ?? "0",
            ["poll.results"] = string.Join(", ", e.Choices.Select(c => $"{c.Title}: {c.Votes}")),
        };
    }

    protected override PollEndedEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            PollId = "ed961efd-8a3f-4cf5-a9d0-e616c590cd2a",
            Title = "Which game next?",
            Status = "completed",
            Choices = [new("c1", "Elden Ring", 14, 3), new("c2", "Hades II", 31, 8)],
            WinningChoiceId = "c2",
        };

    public Task HandleAsync(PollEndedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
