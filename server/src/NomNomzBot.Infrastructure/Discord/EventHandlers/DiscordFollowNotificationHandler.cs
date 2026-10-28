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

/// <summary>The <c>follow</c> Discord trigger: posts the channel's rule when a viewer follows.</summary>
public sealed class DiscordFollowNotificationHandler
    : DiscordTriggerNotificationHandler<FollowEvent>
{
    public DiscordFollowNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        IOutboundSanctionAccessor sanctions,
        ILogger<DiscordFollowNotificationHandler> logger
    )
        : base(dispatcher, sanctions, logger) { }

    protected override string Trigger => "follow";

    protected override string DedupeKeyOf(FollowEvent @event) =>
        $"{Trigger}:{@event.Provider}:{@event.UserId}:{@event.FollowedAt.UtcDateTime:O}";

    protected override Dictionary<string, string> TemplateDataOf(FollowEvent @event) =>
        ViewerData(@event.UserId, @event.UserLogin, @event.UserDisplayName);
}
