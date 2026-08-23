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
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;

namespace NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;

/// <summary>
/// The live half of tenant suspension enforcement (stream-admin.md §3.2, S088): reacts to
/// <see cref="TenantSuspensionChangedEvent"/> immediately instead of waiting for
/// <see cref="BotLifecycleService"/>'s next 5-minute reconcile tick. On suspend/platform-ban this tears down
/// THAT tenant's EventSub session (its full per-channel subscription set — chat read included, so the bot stops
/// reacting to and posting in that channel's chat) without touching any other tenant's subscriptions (one
/// Twitch WS session per Twitch user; <see cref="ITwitchEventSubService.UnsubscribeAllAsync"/> is scoped to a
/// single <c>BroadcasterId</c>). On reinstatement it re-subscribes the same topic set, mirroring
/// <see cref="EventSubSubscribeOnOnboardingHandler"/>'s immediate-seed pattern. Independently resilient — a
/// failure here is caught + logged and BotLifecycleService's own reconcile self-heals on its next tick.
/// </summary>
public sealed class TenantSuspensionEnforcementHandler(
    ITwitchEventSubService eventSub,
    ILogger<TenantSuspensionEnforcementHandler> logger
) : IEventHandler<TenantSuspensionChangedEvent>
{
    public async Task HandleAsync(
        TenantSuspensionChangedEvent @event,
        CancellationToken ct = default
    )
    {
        if (@event.TargetBroadcasterId == Guid.Empty)
            return;

        try
        {
            if (@event.NewStatus == AuthEnums.ChannelStatus.Active)
            {
                Result result = await eventSub.EnsureSubscribedAsync(
                    @event.TargetBroadcasterId,
                    BotLifecycleService.ChannelEventTypes,
                    ct
                );

                if (result.IsSuccess)
                    logger.LogInformation(
                        "Tenant suspension enforcement: reinstated EventSub for {BroadcasterId} — bot resumed",
                        @event.TargetBroadcasterId
                    );
                else
                    logger.LogWarning(
                        "Tenant suspension enforcement: re-subscribe failed for {BroadcasterId}: {Error} ({Code}) — BotLifecycleService's 5-minute reconcile will retry",
                        @event.TargetBroadcasterId,
                        result.ErrorMessage,
                        result.ErrorCode
                    );
            }
            else
            {
                Result result = await eventSub.UnsubscribeAllAsync(@event.TargetBroadcasterId, ct);

                if (result.IsSuccess)
                    logger.LogInformation(
                        "Tenant suspension enforcement: revoked EventSub for {BroadcasterId} (status={Status}) — bot parted, no further chat reads or posts",
                        @event.TargetBroadcasterId,
                        @event.NewStatus
                    );
                else
                    logger.LogWarning(
                        "Tenant suspension enforcement: revoke failed for {BroadcasterId}: {Error} ({Code}) — BotLifecycleService's 5-minute reconcile will retry",
                        @event.TargetBroadcasterId,
                        result.ErrorMessage,
                        result.ErrorCode
                    );
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Tenant suspension enforcement: failed to apply {Status} for {BroadcasterId}",
                @event.NewStatus,
                @event.TargetBroadcasterId
            );
        }
    }
}
