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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity.EventHandlers;

/// <summary>
/// Onboarding seed job (Identity / roles-permissions domain): when a channel finishes onboarding, build the
/// Plane-B management snapshot from Twitch — moderators (badge-sourced) + channel editors (Helix editors) —
/// via <see cref="ITwitchManagementSnapshotBuilder"/> and reconcile the channel's <c>ChannelMemberships</c> so
/// the dashboard's roles screen is populated. <see cref="IMembershipService.SyncManagementFromTwitchAsync"/>
/// idempotently upserts + prunes only the synced rows whose read succeeded (Owner / bot-grant rows untouched).
/// Independently resilient — caught + logged, never propagated, so it cannot affect the other onboarding seed
/// jobs. Safe to run on every onboarding + backfill; the periodic <c>ManagementRoleReconcileService</c> keeps it
/// fresh afterwards (so an editor granted post-onboarding is honoured without a re-onboard).
/// </summary>
public sealed class MembershipSeedOnOnboardingHandler(
    IMembershipService membership,
    ITwitchManagementSnapshotBuilder snapshotBuilder,
    ILogger<MembershipSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (memberships): syncing management roles from Twitch for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            ManagementSnapshot snapshot = await snapshotBuilder.BuildAsync(
                @event.BroadcasterId,
                ct
            );

            Result result = await membership.SyncManagementFromTwitchAsync(
                @event.BroadcasterId,
                snapshot.Members,
                snapshot.AuthoritativeSources,
                ct
            );

            if (result.IsSuccess)
                logger.LogInformation(
                    "Onboarding seed (memberships): completed for {BroadcasterId} ({Count} management member(s))",
                    @event.BroadcasterId,
                    snapshot.Members.Count
                );
            else
                logger.LogWarning(
                    "Onboarding seed (memberships): sync returned a failure for {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (memberships): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
