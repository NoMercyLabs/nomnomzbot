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

/// <summary>Handles new subscription events.</summary>
public sealed class NewSubscriptionEventHandler
    : TwitchAlertHandlerBase<NewSubscriptionEvent>,
        IEventHandler<NewSubscriptionEvent>
{
    protected override string EventTypeKey => "subscription";

    public NewSubscriptionEventHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<NewSubscriptionEventHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(NewSubscriptionEvent e) => e.UserId;

    protected override string? GetUserDisplayName(NewSubscriptionEvent e) => e.UserDisplayName;

    protected override Dictionary<string, string> BuildVariables(NewSubscriptionEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.UserDisplayName,
            ["user.id"] = e.UserId,
            ["tier"] = e.Tier,
        };

    public Task HandleAsync(NewSubscriptionEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>Handles resubscription events.</summary>
public sealed class ResubscriptionEventHandler
    : TwitchAlertHandlerBase<ResubscriptionEvent>,
        IEventHandler<ResubscriptionEvent>
{
    protected override string EventTypeKey => "resub";

    public ResubscriptionEventHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<ResubscriptionEventHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(ResubscriptionEvent e) => e.UserId;

    protected override string? GetUserDisplayName(ResubscriptionEvent e) => e.UserDisplayName;

    protected override Dictionary<string, string> BuildVariables(ResubscriptionEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.UserDisplayName,
            ["user.id"] = e.UserId,
            ["tier"] = e.Tier,
            ["months"] = e.CumulativeMonths.ToString(),
            ["streak"] = e.StreakMonths.ToString(),
            ["message"] = e.Message ?? string.Empty,
        };

    public Task HandleAsync(ResubscriptionEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>Handles gift subscription events.</summary>
public sealed class GiftSubscriptionEventHandler
    : TwitchAlertHandlerBase<GiftSubscriptionEvent>,
        IEventHandler<GiftSubscriptionEvent>
{
    protected override string EventTypeKey => "gift_sub";

    public GiftSubscriptionEventHandler(
        IServiceScopeFactory s,
        IPipelineEngine p,
        ILogger<GiftSubscriptionEventHandler> l
    )
        : base(s, p, l) { }

    protected override string? GetUserId(GiftSubscriptionEvent e) =>
        e.IsAnonymous ? null : e.GifterUserId;

    protected override string? GetUserDisplayName(GiftSubscriptionEvent e) =>
        e.IsAnonymous ? "Anonymous" : e.GifterDisplayName;

    protected override Dictionary<string, string> BuildVariables(GiftSubscriptionEvent e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.IsAnonymous ? "Anonymous" : e.GifterDisplayName,
            ["user.id"] = e.IsAnonymous ? string.Empty : e.GifterUserId,
            ["tier"] = e.Tier,
            ["count"] = e.GiftCount.ToString(),
            ["anonymous"] = e.IsAnonymous ? "true" : "false",
        };

    public Task HandleAsync(GiftSubscriptionEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
