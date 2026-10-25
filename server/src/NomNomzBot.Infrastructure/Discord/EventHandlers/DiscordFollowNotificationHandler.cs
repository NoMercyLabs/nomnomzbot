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
