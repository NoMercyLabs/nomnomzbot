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
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Stream.EventHandlers;

/// <summary>Handles incoming raid events.</summary>
public sealed class RaidEventHandler : TwitchAlertHandlerBase<RaidEvent>, IEventHandler<RaidEvent>
{
    protected override string EventTypeKey => "channel.raid";

    public RaidEventHandler(IServiceScopeFactory s, IPipelineEngine p, ILogger<RaidEventHandler> l)
        : base(s, p, l) { }

    protected override string? GetUserId(RaidEvent e) => e.FromUserId;

    protected override string? GetUserDisplayName(RaidEvent e) => e.FromDisplayName;

    protected override Dictionary<string, string> BuildVariables(RaidEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.FromDisplayName,
            ["user.id"] = e.FromUserId,
            ["user.name"] = e.FromLogin,
            ["viewers"] = e.ViewerCount.ToString(),
        };

    public Task HandleAsync(RaidEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
