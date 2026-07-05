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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;

namespace NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;

/// <summary>
/// Fast EventSub recovery on re-auth (Platform / EventSub domain): the moment a streamer's Twitch token is
/// (re)vaulted — the in-place reconnect after a dead token, or a routine refresh — re-subscribe that channel's
/// full EventSub topic set IMMEDIATELY, instead of waiting up to 5 minutes for <see cref="BotLifecycleService"/>'s
/// next reconcile tick. Without this, a streamer who reconnects a dead token sees chat SEND restored at once but
/// the READ feed stays blind (no follow/sub/cheer/raid/chat events) until that reconcile catches up.
/// <para>
/// Reuses <see cref="BotLifecycleService.ChannelEventTypes"/> — the same topic list the onboarding seed and the
/// reconcile share, so the call sites can never drift — and <see cref="ITwitchEventSubService.EnsureSubscribedAsync"/>,
/// a declarative idempotent reconcile (creates missing, no-ops existing). Firing on every token refresh is therefore
/// safe and cheap: a no-op when nothing is missing, a full recovery when the dead-token gap left subscriptions
/// unregistered. Scoped to the streamer <c>twitch</c> connection — a Spotify/Discord/YouTube or bot-account
/// (<c>twitch_bot</c>) refresh does not own the per-channel topics, and a broadcaster-less connection has no
/// channel to reconcile. Independently resilient — caught + logged, never propagated.
/// </para>
/// </summary>
public sealed class EventSubResubscribeOnTokenRefreshedHandler(
    ITwitchEventSubService eventSub,
    ILogger<EventSubResubscribeOnTokenRefreshedHandler> logger
) : IEventHandler<IntegrationTokenRefreshedEvent>
{
    public async Task HandleAsync(
        IntegrationTokenRefreshedEvent @event,
        CancellationToken ct = default
    )
    {
        // Only the streamer Twitch connection owns the per-channel EventSub topics; a bot-account (twitch_bot) or
        // a Spotify/Discord/YouTube refresh does not, and a broadcaster-less connection has no channel to reconcile.
        if (
            @event.Provider != AuthEnums.IntegrationProvider.Twitch
            || @event.BroadcasterId == Guid.Empty
        )
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
                    "Re-auth EventSub recovery: re-subscribed {BroadcasterId} to its EventSub topic set on token (re)vault",
                    @event.BroadcasterId
                );
            else
                logger.LogWarning(
                    "Re-auth EventSub recovery: subscribe returned a failure for {BroadcasterId}: {Error} ({Code}) — BotLifecycleService's 5-minute reconcile will retry",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Re-auth EventSub recovery: failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
