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
/// Dispatches the <c>channel.ad_break.begin</c> event response — the "ads incoming, stretch break" chat notice.
/// Variables: <c>{ad.duration}</c> (seconds), <c>{ad.automatic}</c> (true/false), and the requester as
/// <c>{user}</c>/<c>{user.id}</c> (empty for an automatic break — Twitch sends no requester then).
/// </summary>
public sealed class AdBreakBeganAlertHandler
    : TwitchAlertHandlerBase<AdBreakBeganEvent>,
        IEventHandler<AdBreakBeganEvent>
{
    protected override string EventTypeKey => "channel.ad_break.begin";

    public AdBreakBeganAlertHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<AdBreakBeganAlertHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(AdBreakBeganEvent e) => e.RequesterUserId;

    protected override string? GetUserDisplayName(AdBreakBeganEvent e) => e.RequesterDisplayName;

    protected override Dictionary<string, string> BuildVariables(AdBreakBeganEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.RequesterDisplayName ?? string.Empty,
            ["user.id"] = e.RequesterUserId ?? string.Empty,
            ["ad.duration"] = e.DurationSeconds.ToString(),
            ["ad.automatic"] = e.IsAutomatic ? "true" : "false",
        };

    public Task HandleAsync(AdBreakBeganEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
