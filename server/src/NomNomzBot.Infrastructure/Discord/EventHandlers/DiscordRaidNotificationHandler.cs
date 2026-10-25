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
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Discord.EventHandlers;

/// <summary>The <c>raid</c> Discord trigger: posts the channel's rule when another channel raids it.</summary>
public sealed class DiscordRaidNotificationHandler : DiscordTriggerNotificationHandler<RaidEvent>
{
    public DiscordRaidNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        IOutboundSanctionAccessor sanctions,
        ILogger<DiscordRaidNotificationHandler> logger
    )
        : base(dispatcher, sanctions, logger) { }

    protected override string Trigger => "raid";

    protected override string DedupeKeyOf(RaidEvent @event) =>
        $"{Trigger}:{@event.FromUserId}:{@event.OccurredAt.UtcDateTime:O}";

    protected override Dictionary<string, string> TemplateDataOf(RaidEvent @event)
    {
        Dictionary<string, string> data = ViewerData(
            @event.FromUserId,
            @event.FromLogin,
            @event.FromDisplayName
        );
        data["viewers"] = @event.ViewerCount.ToString();
        return data;
    }
}
