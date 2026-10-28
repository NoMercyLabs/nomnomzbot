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
