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
            // An automatic ad break has no requester: the keys read as none instead of printing their braces.
            ["user"] = e.RequesterDisplayName ?? string.Empty,
            ["user.id"] = e.RequesterUserId ?? string.Empty,
            ["ad.duration"] = HumanDuration(e.DurationSeconds),
            ["ad.automatic"] = e.IsAutomatic ? "true" : "false",
        };

    protected override AdBreakBeganEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            DurationSeconds = 180,
            IsAutomatic = true,
            StartedAt = now,
        };

    public Task HandleAsync(AdBreakBeganEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
