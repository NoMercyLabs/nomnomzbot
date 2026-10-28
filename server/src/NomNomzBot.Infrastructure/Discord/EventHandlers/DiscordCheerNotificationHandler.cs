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

    protected override Dictionary<string, string> TemplateDataOf(CheerEvent @event)
    {
        // An anonymous cheer has no login or id to show: the key holds the placeholder name only.
        Dictionary<string, string> data = @event.IsAnonymous
            ? new() { ["user"] = "Anonymous" }
            : ViewerData(@event.UserId, @event.UserLogin, @event.UserDisplayName);
        data["bits"] = @event.Bits.ToString();
        data["message"] = @event.Message;
        return data;
    }
}
