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

    protected override Dictionary<string, string> TemplateDataOf(ResubscriptionEvent @event) =>
        new()
        {
            ["user.name"] = @event.UserDisplayName,
            ["tier"] = DiscordTierLabel.Of(@event.Tier),
            ["months"] = @event.CumulativeMonths.ToString(),
            ["streak"] = @event.StreakMonths.ToString(),
            ["message"] = @event.Message ?? string.Empty,
        };
}
