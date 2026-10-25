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
