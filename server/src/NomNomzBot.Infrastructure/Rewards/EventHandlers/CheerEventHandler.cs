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
    protected override string EventTypeKey => "channel.cheer";

    public CheerEventHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<CheerEventHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(CheerEvent e) => e.IsAnonymous ? null : e.UserId;

    protected override string? GetUserDisplayName(CheerEvent e) =>
        e.IsAnonymous ? "Anonymous" : e.UserDisplayName;

    protected override string? GetUserLogin(CheerEvent e) => e.IsAnonymous ? null : e.UserLogin;

    protected override Dictionary<string, string> BuildVariables(CheerEvent e)
    {
        string message = CheermoteText.Strip(e.Message);
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["user.id"] = e.IsAnonymous ? string.Empty : e.UserId,
            ["bits"] = e.Bits.ToString(),
            ["message"] = message,
            ["also_said"] = AlsoSaidClause.From(message),
            ["anonymous"] = e.IsAnonymous ? "true" : "false",
            ["provider"] = e.Provider,
        };
    }

    protected override CheerEvent SampleEvent(DateTimeOffset now) =>
        new()
        {
            UserId = "100000002",
            UserDisplayName = "BitBaron",
            UserLogin = "bitbaron",
            Bits = 500,
            Message = "Cheer500 keep it up!",
            IsAnonymous = false,
        };

    public Task HandleAsync(CheerEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
