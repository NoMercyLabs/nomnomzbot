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

namespace NomNomzBot.Infrastructure.Widgets.EventHandlers;

/// <summary>
/// Onboarding seed job (Widgets domain, S052 — widgets-overlays.md §1.2): when a channel finishes onboarding,
/// provision its <c>tts_caption</c> system surface immediately, rather than waiting for the streamer to first
/// open the TTS page. <see cref="IWidgetService.EnsureSystemWidgetAsync"/> is the same get-or-create the TTS
/// page's "on first use" leg already calls, so this only changes WHEN it fires for a fresh channel — it is
/// idempotent either way, and safe to re-run on the onboarding backfill. Independently resilient — a failure
/// here is caught and logged, never propagated, so it cannot affect the other onboarding seed jobs.
/// </summary>
public sealed class TtsSystemWidgetSeedOnOnboardingHandler(
    IWidgetService widgets,
    ILogger<TtsSystemWidgetSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    private const string TtsCaptionNaturalKey = "tts_caption";

    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (widgets): provisioning the tts_caption system surface for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            Result<WidgetDetail> result = await widgets.EnsureSystemWidgetAsync(
                @event.BroadcasterId.ToString(),
                TtsCaptionNaturalKey,
                ct
            );

            if (result.IsFailure)
                logger.LogWarning(
                    "Onboarding seed (widgets): tts_caption provisioning returned a failure for {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
            else
                logger.LogInformation(
                    "Onboarding seed (widgets): completed for {BroadcasterId}",
                    @event.BroadcasterId
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (widgets): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
