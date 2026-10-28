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
/// The <c>subscription</c> Discord trigger: posts the channel's rule when a viewer subscribes. A sub that was
/// gifted to the viewer is left to the <c>gift_subscription</c> trigger, so one gift never posts twice.
/// </summary>
public sealed class DiscordSubscriptionNotificationHandler
    : DiscordTriggerNotificationHandler<NewSubscriptionEvent>
{
    public DiscordSubscriptionNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        IOutboundSanctionAccessor sanctions,
        ILogger<DiscordSubscriptionNotificationHandler> logger
    )
        : base(dispatcher, sanctions, logger) { }

    protected override string Trigger => "subscription";

    protected override bool IsSkipped(NewSubscriptionEvent @event) => @event.IsGift;

    protected override string DedupeKeyOf(NewSubscriptionEvent @event) =>
        $"{Trigger}:{@event.Provider}:{@event.UserId}:{@event.OccurredAt.UtcDateTime:O}";

    protected override Dictionary<string, string> TemplateDataOf(NewSubscriptionEvent @event)
    {
        Dictionary<string, string> data = ViewerData(
            @event.UserId,
            @event.UserLogin,
            @event.UserDisplayName
        );
        data["tier"] = DiscordTierLabel.Of(@event.Tier);
        return data;
    }
}
