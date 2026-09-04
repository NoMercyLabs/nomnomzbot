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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Widgets.EventHandlers;

/// <summary>
/// Onboarding seed job (Widgets domain, S052 — widgets-overlays.md §1.2): when a channel finishes onboarding,
/// provision every channel-owned system surface (<see cref="FirstPartyWidgetCatalogue.SystemSurfaceNaturalKeys"/>
/// — currently <c>tts_caption</c> and <c>alerts</c>) immediately, rather than waiting for the streamer to first
/// open each surface's owner page. <see cref="IWidgetService.EnsureSystemWidgetAsync"/> is the same get-or-create
/// each owner page's "on first use" leg already calls, so this only changes WHEN it fires for a fresh channel —
/// it is idempotent either way, and safe to re-run on the onboarding backfill. One surface's failure is caught
/// and logged, never propagated, so it cannot block the others or affect the other onboarding seed jobs.
/// </summary>
public sealed class SystemWidgetSeedOnOnboardingHandler(
    IWidgetService widgets,
    ILogger<SystemWidgetSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        foreach (string naturalKey in FirstPartyWidgetCatalogue.SystemSurfaceNaturalKeys)
        {
            logger.LogInformation(
                "Onboarding seed (widgets): provisioning the {NaturalKey} system surface for {BroadcasterId} ({Name})",
                naturalKey,
                @event.BroadcasterId,
                @event.Name
            );

            try
            {
                Result<WidgetDetail> result = await widgets.EnsureSystemWidgetAsync(
                    @event.BroadcasterId.ToString(),
                    naturalKey,
                    ct
                );

                if (result.IsFailure)
                    logger.LogWarning(
                        "Onboarding seed (widgets): {NaturalKey} provisioning returned a failure for {BroadcasterId}: {Error} ({Code})",
                        naturalKey,
                        @event.BroadcasterId,
                        result.ErrorMessage,
                        result.ErrorCode
                    );
                else
                    logger.LogInformation(
                        "Onboarding seed (widgets): {NaturalKey} completed for {BroadcasterId}",
                        naturalKey,
                        @event.BroadcasterId
                    );
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(
                    ex,
                    "Onboarding seed (widgets): {NaturalKey} failed for {BroadcasterId}",
                    naturalKey,
                    @event.BroadcasterId
                );
            }
        }
    }
}
