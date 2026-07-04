// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;

namespace NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;

/// <summary>
/// Onboarding seed job (Platform / EventSub domain): subscribes the newly-onboarded channel to its full
/// per-channel EventSub topic set immediately, instead of waiting for <see cref="BotLifecycleService"/>'s next
/// 5-minute reconcile tick — a new channel must not sit "blind" (no follow/sub/cheer/raid/chat events) for up
/// to 5 minutes after onboarding. Reuses <see cref="BotLifecycleService.ChannelEventTypes"/> — already
/// <c>internal</c> (assembly-visible) precisely so a second call site can share it — instead of duplicating the
/// topic list, so the two call sites can never drift apart. <see cref="ITwitchEventSubService.EnsureSubscribedAsync"/>
/// is a declarative reconcile (idempotent: creates missing, no-ops existing), so this is safe to run again on
/// re-auth or the startup backfill; <see cref="BotLifecycleService"/>'s own reconcile simply finds nothing left
/// to do on its next tick for a channel this handler already subscribed. Independently resilient — caught +
/// logged, never propagated, so it cannot affect the other onboarding seed jobs.
/// </summary>
public sealed class EventSubSubscribeOnOnboardingHandler(
    ITwitchEventSubService eventSub,
    ILogger<EventSubSubscribeOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            Result result = await eventSub.EnsureSubscribedAsync(
                @event.BroadcasterId,
                BotLifecycleService.ChannelEventTypes,
                ct
            );

            if (result.IsSuccess)
                logger.LogInformation(
                    "Onboarding seed (EventSub): subscribed {BroadcasterId} ({Name}) to its EventSub topic set",
                    @event.BroadcasterId,
                    @event.Name
                );
            else
                logger.LogWarning(
                    "Onboarding seed (EventSub): subscribe returned a failure for {BroadcasterId}: {Error} ({Code}) — BotLifecycleService's 5-minute reconcile will retry",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (EventSub): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
