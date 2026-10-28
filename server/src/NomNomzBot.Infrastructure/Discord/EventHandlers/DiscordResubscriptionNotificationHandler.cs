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
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Discord.EventHandlers;

/// <summary>
/// The <c>resubscription</c> Discord trigger: posts the channel's rule when a viewer renews a subscription and
/// shares it (Twitch subscription message, Kick renewal, YouTube membership milestone). A first-time sub is
/// the <c>subscription</c> trigger, so one sub never posts under both.
/// </summary>
public sealed class DiscordResubscriptionNotificationHandler
    : DiscordTriggerNotificationHandler<ResubscriptionEvent>
{
    public DiscordResubscriptionNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        IOutboundSanctionAccessor sanctions,
        ILogger<DiscordResubscriptionNotificationHandler> logger
    )
        : base(dispatcher, sanctions, logger) { }

    protected override string Trigger => "resubscription";

    protected override string DedupeKeyOf(ResubscriptionEvent @event) =>
        $"{Trigger}:{@event.Provider}:{@event.UserId}:{@event.OccurredAt.UtcDateTime:O}:{@event.CumulativeMonths}";

    protected override Dictionary<string, string> TemplateDataOf(ResubscriptionEvent @event)
    {
        Dictionary<string, string> data = ViewerData(
            @event.UserId,
            @event.UserLogin,
            @event.UserDisplayName
        );
        data["tier"] = DiscordTierLabel.Of(@event.Tier);
        data["months"] = @event.CumulativeMonths.ToString();
        data["streak"] = @event.StreakMonths.ToString();
        data["message"] = @event.Message ?? string.Empty;
        return data;
    }
}
