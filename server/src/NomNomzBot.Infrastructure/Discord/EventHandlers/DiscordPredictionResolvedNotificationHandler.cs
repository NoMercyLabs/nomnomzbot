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
/// The <c>prediction_resolved</c> Discord trigger: posts the channel's rule when a prediction is resolved with
/// a winning outcome. A cancelled prediction has no winner and stays silent. The prediction's own id is the
/// dedupe key.
/// </summary>
public sealed class DiscordPredictionResolvedNotificationHandler
    : DiscordTriggerNotificationHandler<PredictionEndedEvent>
{
    private const string ResolvedStatus = "resolved";

    public DiscordPredictionResolvedNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        IOutboundSanctionAccessor sanctions,
        ILogger<DiscordPredictionResolvedNotificationHandler> logger
    )
        : base(dispatcher, sanctions, logger) { }

    protected override string Trigger => "prediction_resolved";

    protected override bool IsSkipped(PredictionEndedEvent @event) =>
        !string.Equals(@event.Status, ResolvedStatus, StringComparison.OrdinalIgnoreCase)
        || FindWinner(@event) is null;

    protected override string DedupeKeyOf(PredictionEndedEvent @event) =>
        $"{Trigger}:{@event.PredictionId}";

    protected override Dictionary<string, string> TemplateDataOf(PredictionEndedEvent @event)
    {
        PredictionOutcome winner = FindWinner(@event)!;

        return new()
        {
            ["prediction.title"] = @event.Title,
            ["prediction.winner"] = winner.Title,
            ["prediction.winner.points"] = winner.ChannelPoints.ToString(),
            ["prediction.winner.users"] = winner.Users.ToString(),
        };
    }

    private static PredictionOutcome? FindWinner(PredictionEndedEvent @event) =>
        @event.Outcomes.FirstOrDefault(outcome => outcome.Id == @event.WinningOutcomeId);
}
