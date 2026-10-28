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

/// <summary>Handles incoming raid events.</summary>
public sealed class RaidEventHandler : TwitchAlertHandlerBase<RaidEvent>, IEventHandler<RaidEvent>
{
    protected override string EventTypeKey => "channel.raid";

    public RaidEventHandler(IServiceScopeFactory s, IPipelineEngine p, ILogger<RaidEventHandler> l)
        : base(s, p, l) { }

    protected override string? GetUserId(RaidEvent e) => e.FromUserId;

    protected override string? GetUserDisplayName(RaidEvent e) => e.FromDisplayName;

    protected override string? GetUserLogin(RaidEvent e) => e.FromLogin;

    protected override Dictionary<string, string> BuildVariables(RaidEvent e) =>
        new(StringComparer.OrdinalIgnoreCase) { ["viewers"] = e.ViewerCount.ToString() };

    protected override RaidEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            FromUserId = "100000014",
            FromDisplayName = "RaidRider",
            FromLogin = "raidrider",
            ViewerCount = 42,
        };

    public Task HandleAsync(RaidEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
