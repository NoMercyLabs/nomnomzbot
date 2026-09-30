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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// Onboarding job: a channel that joins after a network block was applied bans the blocked actor straight
/// away, instead of waiting for them to chat. Idempotent (one leg per block per channel), so the startup
/// backfill that re-publishes onboarding adds nothing twice.
/// </summary>
public sealed class NetworkBlockOnOnboardingHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<NetworkBlockOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            Result<int> enforced = await scope
                .ServiceProvider.GetRequiredService<INetworkBlockEnforcementService>()
                .EnforceForOnboardedChannelAsync(@event.BroadcasterId, ct);

            if (enforced.IsFailure)
                logger.LogWarning(
                    "Network block onboarding check failed for {Channel}: {Error}",
                    @event.BroadcasterId,
                    enforced.ErrorMessage
                );
            else if (enforced.Value > 0)
                logger.LogInformation(
                    "Network block: {Count} blocked actor(s) banned in newly onboarded {Channel}",
                    enforced.Value,
                    @event.BroadcasterId
                );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Network block onboarding check failed for {Channel}",
                @event.BroadcasterId
            );
        }
    }
}
