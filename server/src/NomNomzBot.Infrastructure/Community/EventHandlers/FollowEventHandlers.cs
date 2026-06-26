// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Community.EventHandlers;

/// <summary>Handles follow events from EventSub.</summary>
public sealed class FollowEventHandler
    : TwitchAlertHandlerBase<FollowEvent>,
        IEventHandler<FollowEvent>
{
    protected override string EventTypeKey => "channel.follow";

    public FollowEventHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<FollowEventHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(FollowEvent e) => e.UserId;

    protected override string? GetUserDisplayName(FollowEvent e) => e.UserDisplayName;

    protected override Dictionary<string, string> BuildVariables(FollowEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.UserDisplayName,
            ["user.id"] = e.UserId,
            ["user.name"] = e.UserLogin,
            ["followed_at"] = e.FollowedAt.ToString("O"),
        };

    public Task HandleAsync(FollowEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>Handles new follower events from the IRC path (duplicate of FollowEvent).</summary>
public sealed class NewFollowerEventHandler
    : TwitchAlertHandlerBase<NewFollowerEvent>,
        IEventHandler<NewFollowerEvent>
{
    protected override string EventTypeKey => "channel.follow";

    public NewFollowerEventHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<NewFollowerEventHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(NewFollowerEvent e) => e.UserId;

    protected override string? GetUserDisplayName(NewFollowerEvent e) => e.UserDisplayName;

    protected override Dictionary<string, string> BuildVariables(NewFollowerEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.UserDisplayName,
            ["user.id"] = e.UserId,
            ["user.name"] = e.UserLogin,
        };

    public Task HandleAsync(NewFollowerEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
