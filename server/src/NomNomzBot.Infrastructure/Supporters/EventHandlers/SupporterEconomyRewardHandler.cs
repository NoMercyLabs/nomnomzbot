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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Supporters.Events;

namespace NomNomzBot.Infrastructure.Supporters.EventHandlers;

/// <summary>
/// The opt-in economy reward for a supporter event (supporter-events.md D5). When a normalized
/// <see cref="SupporterEventReceived"/> is attributed to a known viewer, this hands it to the economy earning
/// engine as an <see cref="EarningSource.Supporter"/> earn — so it reuses that engine's rule resolution
/// (off unless the streamer configured a <c>Supporter</c> earning rule), role gate, per-window/per-stream caps,
/// and — keyed on the <see cref="SupporterEventReceived.SupporterEventId"/> — its idempotency, so a redelivered
/// webhook credits at most once. Nothing to credit (no matched viewer) is a no-op; a failure is logged, never
/// thrown — the reward is a side benefit and must not break ingest.
/// </summary>
public sealed class SupporterEconomyRewardHandler : IEventHandler<SupporterEventReceived>
{
    private readonly ICurrencyEarningService _earning;
    private readonly ILogger<SupporterEconomyRewardHandler> _logger;

    public SupporterEconomyRewardHandler(
        ICurrencyEarningService earning,
        ILogger<SupporterEconomyRewardHandler> logger
    )
    {
        _earning = earning;
        _logger = logger;
    }

    public async Task HandleAsync(
        SupporterEventReceived @event,
        CancellationToken cancellationToken = default
    )
    {
        // A reward can only be credited to a resolved viewer; an unmatched supporter has no account.
        if (@event.BroadcasterId == Guid.Empty || @event.SupporterUserId is not Guid viewerId)
            return;

        try
        {
            EarnRequest request = new(
                ViewerUserId: viewerId,
                Source: nameof(EarningSource.Supporter),
                Units: 1,
                EventId: @event.SupporterEventId,
                ViewerRoleLevel: null,
                Context: null
            );

            Result<long> result = await _earning.ApplyEarningAsync(
                @event.BroadcasterId,
                request,
                cancellationToken
            );

            if (result.IsSuccess && result.Value > 0)
                _logger.LogInformation(
                    "Supporter reward: credited {Amount} to {Viewer} for {Kind} on {Channel}",
                    result.Value,
                    viewerId,
                    @event.Kind,
                    @event.BroadcasterId
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Supporter economy reward failed for {Channel} (event {EventId})",
                @event.BroadcasterId,
                @event.SupporterEventId
            );
        }
    }
}
