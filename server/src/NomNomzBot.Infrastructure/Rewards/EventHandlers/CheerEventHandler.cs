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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>Handles bits/cheer events.</summary>
public sealed class CheerEventHandler
    : TwitchAlertHandlerBase<CheerEvent>,
        IEventHandler<CheerEvent>
{
    protected override string EventTypeKey => "cheer";

    public CheerEventHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<CheerEventHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(CheerEvent e) => e.IsAnonymous ? null : e.UserId;

    protected override string? GetUserDisplayName(CheerEvent e) =>
        e.IsAnonymous ? "Anonymous" : e.UserDisplayName;

    protected override Dictionary<string, string> BuildVariables(CheerEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.IsAnonymous ? "Anonymous" : e.UserDisplayName,
            ["user.id"] = e.IsAnonymous ? string.Empty : e.UserId,
            ["bits"] = e.Bits.ToString(),
            ["message"] = e.Message,
            ["anonymous"] = e.IsAnonymous ? "true" : "false",
        };

    public Task HandleAsync(CheerEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
