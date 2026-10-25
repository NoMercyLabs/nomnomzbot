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
/// The <c>gift_subscription</c> Discord trigger: posts the channel's rule once per gift batch. An anonymous
/// gifter shows as "Anonymous".
/// </summary>
public sealed class DiscordGiftSubscriptionNotificationHandler
    : DiscordTriggerNotificationHandler<GiftSubscriptionEvent>
{
    public DiscordGiftSubscriptionNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        IOutboundSanctionAccessor sanctions,
        ILogger<DiscordGiftSubscriptionNotificationHandler> logger
    )
        : base(dispatcher, sanctions, logger) { }

    protected override string Trigger => "gift_subscription";

    protected override string DedupeKeyOf(GiftSubscriptionEvent @event) =>
        $"{Trigger}:{@event.Provider}:{@event.GifterUserId}:{@event.OccurredAt.UtcDateTime:O}:{@event.GiftCount}";

    protected override Dictionary<string, string> TemplateDataOf(GiftSubscriptionEvent @event)
    {
        Dictionary<string, string> data = @event.IsAnonymous
            ? new() { ["user"] = "Anonymous" }
            : ViewerData(@event.GifterUserId, @event.GifterLogin, @event.GifterDisplayName);
        data["count"] = @event.GiftCount.ToString();
        data["tier"] = DiscordTierLabel.Of(@event.Tier);
        return data;
    }
}
