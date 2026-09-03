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
/// Dispatches the <c>channel.raid.start</c> event response — the raid countdown has begun.
///
/// <para>This is the moment <c>channel.raid.out</c> used to fire on, which is why a raid pipeline that
/// stopped the stream ended the broadcast at the START of the countdown. <c>channel.raid.out</c> now means
/// the raid has executed; this event keeps the countdown moment reactable, which is where an outro scene,
/// a goodbye message, or a "we are heading to X" overlay belongs.</para>
///
/// <para><c>{user}</c> et al. name the TARGET channel, matching <see cref="OutgoingRaidAlertHandler"/> so a
/// streamer can move a response between the two without rewriting its template.</para>
/// </summary>
public sealed class OutgoingRaidStartedAlertHandler
    : TwitchAlertHandlerBase<OutgoingRaidStartedEvent>,
        IEventHandler<OutgoingRaidStartedEvent>
{
    protected override string EventTypeKey => "channel.raid.start";

    public OutgoingRaidStartedAlertHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<OutgoingRaidStartedAlertHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(OutgoingRaidStartedEvent e) => e.ToUserId;

    protected override string? GetUserDisplayName(OutgoingRaidStartedEvent e) => e.ToDisplayName;

    protected override Dictionary<string, string> BuildVariables(OutgoingRaidStartedEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.ToDisplayName,
            ["user.id"] = e.ToUserId,
            ["user.name"] = e.ToLogin,
            ["viewers"] = e.ViewerCount.ToString(),
        };

    public Task HandleAsync(OutgoingRaidStartedEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
