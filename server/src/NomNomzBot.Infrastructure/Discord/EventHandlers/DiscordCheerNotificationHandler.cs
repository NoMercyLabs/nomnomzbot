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
/// The <c>cheer</c> Discord trigger: posts the channel's rule when a viewer cheers. An anonymous cheer
/// shows as "Anonymous".
/// </summary>
public sealed class DiscordCheerNotificationHandler : DiscordTriggerNotificationHandler<CheerEvent>
{
    public DiscordCheerNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        IOutboundSanctionAccessor sanctions,
        ILogger<DiscordCheerNotificationHandler> logger
    )
        : base(dispatcher, sanctions, logger) { }

    protected override string Trigger => "cheer";

    protected override string DedupeKeyOf(CheerEvent @event) =>
        $"{Trigger}:{@event.Provider}:{@event.UserId}:{@event.OccurredAt.UtcDateTime:O}:{@event.Bits}";

    protected override Dictionary<string, string> TemplateDataOf(CheerEvent @event) =>
        new()
        {
            ["user.name"] = @event.IsAnonymous ? "Anonymous" : @event.UserDisplayName,
            ["bits"] = @event.Bits.ToString(),
            ["message"] = @event.Message,
        };
}
