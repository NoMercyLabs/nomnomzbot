// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Community.Events;

namespace NomNomzBot.Infrastructure.Discord.EventHandlers;

/// <summary>
/// The <c>poll_end</c> Discord trigger: posts the channel's rule when a poll ran to its end. A poll that was
/// terminated or archived stays silent, like its chat response. The poll's own id is the dedupe key.
/// </summary>
public sealed class DiscordPollEndNotificationHandler
    : DiscordTriggerNotificationHandler<PollEndedEvent>
{
    private const string CompletedStatus = "completed";
    private const string NoWinner = "nobody";

    public DiscordPollEndNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        IOutboundSanctionAccessor sanctions,
        ILogger<DiscordPollEndNotificationHandler> logger
    )
        : base(dispatcher, sanctions, logger) { }

    protected override string Trigger => "poll_end";

    protected override bool IsSkipped(PollEndedEvent @event) =>
        !string.Equals(@event.Status, CompletedStatus, StringComparison.OrdinalIgnoreCase);

    protected override string DedupeKeyOf(PollEndedEvent @event) => $"{Trigger}:{@event.PollId}";

    protected override Dictionary<string, string> TemplateDataOf(PollEndedEvent @event)
    {
        PollChoice? winner = @event.Choices.FirstOrDefault(choice =>
            choice.Id == @event.WinningChoiceId
        );

        return new()
        {
            ["poll.title"] = @event.Title,
            ["poll.winner"] = winner?.Title ?? NoWinner,
            ["poll.winner.votes"] = (winner?.Votes ?? 0).ToString(),
            ["poll.results"] = string.Join(
                " | ",
                @event.Choices.Select(choice => $"{choice.Title}: {choice.Votes}")
            ),
        };
    }
}
