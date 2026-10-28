// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
