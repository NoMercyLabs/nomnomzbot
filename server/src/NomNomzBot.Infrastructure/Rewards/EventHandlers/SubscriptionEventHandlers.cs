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

/// <summary>Twitch's own sub-tier codes ("1000"/"2000"/"3000"/"Prime") are not what a viewer reads as a tier —
/// the {{tier}} template variable must carry the human label ("1"/"2"/"3"/"Prime"), never the raw Helix code.</summary>
internal static class TwitchSubTier
{
    public static string ToLabel(string? tier) =>
        tier switch
        {
            "1000" => "1",
            "2000" => "2",
            "3000" => "3",
            "Prime" or "prime" => "Prime",
            _ => tier ?? string.Empty,
        };
}

/// <summary>Handles new subscription events.</summary>
public sealed class NewSubscriptionEventHandler
    : TwitchAlertHandlerBase<NewSubscriptionEvent>,
        IEventHandler<NewSubscriptionEvent>
{
    protected override string EventTypeKey => "channel.subscribe";

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
            ["tier"] = TwitchSubTier.ToLabel(e.Tier),
            ["provider"] = e.Provider,
        };

    public Task HandleAsync(NewSubscriptionEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>Handles resubscription events.</summary>
public sealed class ResubscriptionEventHandler
    : TwitchAlertHandlerBase<ResubscriptionEvent>,
        IEventHandler<ResubscriptionEvent>
{
    protected override string EventTypeKey => "channel.subscription.message";

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
            ["tier"] = TwitchSubTier.ToLabel(e.Tier),
            ["months"] = e.CumulativeMonths.ToString(),
            ["streak"] = e.StreakMonths.ToString(),
            ["message"] = e.Message ?? string.Empty,
            // Pre-formatted "they also said" addendum — the template engine has no {{#if}} block syntax
            // (TemplateResolver.VariablePattern is flat substitution only), so an optional fragment is
            // carried as a variable that already resolves to the full clause, or to an empty string when
            // the resubscriber didn't write anything — matching {tier}/{stream.uptime}'s own pattern of a
            // fully pre-formatted, never-raw value. Wording matches the old bot's resub/cheer/watch-streak
            // "They also said: ..." convention (NoMercyBot.Services.Twitch.WatchStreakService).
            ["also_said"] = string.IsNullOrWhiteSpace(e.Message)
                ? string.Empty
                : $" They also said: \"{e.Message}\"",
            ["provider"] = e.Provider,
        };

    public Task HandleAsync(ResubscriptionEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}

/// <summary>Handles gift subscription events.</summary>
public sealed class GiftSubscriptionEventHandler
    : TwitchAlertHandlerBase<GiftSubscriptionEvent>,
        IEventHandler<GiftSubscriptionEvent>
{
    protected override string EventTypeKey => "channel.subscription.gift";

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
            ["tier"] = TwitchSubTier.ToLabel(e.Tier),
            ["count"] = e.GiftCount.ToString(),
            ["anonymous"] = e.IsAnonymous ? "true" : "false",
            ["provider"] = e.Provider,
        };

    public Task HandleAsync(GiftSubscriptionEvent @event, CancellationToken ct = default) =>
        HandleCoreAsync(@event, ct);
}
