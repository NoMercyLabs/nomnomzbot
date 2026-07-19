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

/// <summary>
/// Dispatches the <c>channel.raid.out</c> event response — this channel raiding OUT to another broadcaster
/// (the incoming direction is <c>channel.raid</c> via <see cref="RaidEventHandler"/>). <c>{user}</c> et al.
/// name the TARGET channel being raided, mirroring how the incoming preset's <c>{user}</c> names the raider.
/// </summary>
public sealed class OutgoingRaidAlertHandler
    : TwitchAlertHandlerBase<OutgoingRaidEvent>,
        IEventHandler<OutgoingRaidEvent>
{
    protected override string EventTypeKey => "channel.raid.out";

    public OutgoingRaidAlertHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<OutgoingRaidAlertHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(OutgoingRaidEvent e) => e.ToUserId;

    protected override string? GetUserDisplayName(OutgoingRaidEvent e) => e.ToDisplayName;

    protected override Dictionary<string, string> BuildVariables(OutgoingRaidEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.ToDisplayName,
            ["user.id"] = e.ToUserId,
            ["user.name"] = e.ToLogin,
            ["viewers"] = e.ViewerCount.ToString(),
        };

    public Task HandleAsync(OutgoingRaidEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
