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
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// Puts the follow-bot track on the live follow path: each Twitch follow feeds the channel's baseline,
/// and a spike over it hands the window to <see cref="FollowBotSweepService"/>. Twitch only — block is
/// a Helix action and Kick has no equivalent.
/// </summary>
public sealed class FollowSpikeHandler : IEventHandler<FollowEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FollowSpikeTracker _tracker;
    private readonly ILogger<FollowSpikeHandler> _logger;

    public FollowSpikeHandler(
        IServiceScopeFactory scopeFactory,
        FollowSpikeTracker tracker,
        ILogger<FollowSpikeHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _tracker = tracker;
        _logger = logger;
    }

    public async Task HandleAsync(FollowEvent @event, CancellationToken ct)
    {
        if (@event.BroadcasterId == Guid.Empty || @event.Provider != AuthEnums.Platform.Twitch)
            return;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            SpamDefenseSettings settings = await scope
                .ServiceProvider.GetRequiredService<ISpamDefenseService>()
                .GetSettingsAsync(@event.BroadcasterId, ct);
            if (!settings.IsEnabled)
                return;

            FollowSpikeWindow? window = _tracker.Observe(
                @event.BroadcasterId,
                new FollowObservation(
                    @event.UserId,
                    @event.UserLogin,
                    @event.UserDisplayName,
                    @event.FollowedAt
                ),
                settings.FollowSpikeFactor
            );
            if (window is null)
                return;

            FollowBotSweepOutcome outcome = await scope
                .ServiceProvider.GetRequiredService<FollowBotSweepService>()
                .SweepAsync(@event.BroadcasterId, window, settings, ct);

            _logger.LogWarning(
                "Follow spike in {Channel}: examined {Examined}, flagged {Flagged}, blocked {Blocked}, "
                    + "failed {Failed}, dryRun={DryRun}, batch {Batch}",
                @event.BroadcasterId,
                outcome.Examined,
                outcome.Flagged,
                outcome.Blocked,
                outcome.Failed,
                outcome.DryRun,
                window.BatchId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Follow-bot sweep failed for {Channel}", @event.BroadcasterId);
        }
    }
}
