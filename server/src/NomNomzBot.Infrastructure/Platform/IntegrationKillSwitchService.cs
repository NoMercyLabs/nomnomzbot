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
using NomNomzBot.Application.Abstractions.Platform;

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>
/// The per-integration kill switch (rollout-updates §5) — thin wrapper over <see cref="IFeatureFlagService"/>
/// using the <c>integration:{name}</c> flag-key convention, so the existing FeatureFlagAdminController is the
/// one write surface for both staged-rollout flags AND integration kill switches; nothing new to administer.
/// The wrapper's entire reason to exist is the try/catch below: a dependent feature must NEVER see an
/// exception out of this call, only a clean <see cref="IntegrationAvailability"/>.
/// </summary>
public sealed class IntegrationKillSwitchService(
    IFeatureFlagService featureFlags,
    ILogger<IntegrationKillSwitchService> logger
) : IIntegrationKillSwitchService
{
    private const string FlagKeyPrefix = "integration:";

    public async Task<IntegrationAvailability> CheckAsync(
        string integrationKey,
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        try
        {
            FeatureFlagEvaluation eval = await featureFlags.EvaluateAsync(
                FlagKeyPrefix + integrationKey,
                broadcasterId,
                ct
            );

            // Opt-OUT semantics: no flag defined for this integration means it was never killed, so it is
            // available — the mirror image of the staged-rollout default (fail closed on an undefined flag).
            return !eval.Exists || eval.Enabled
                ? new IntegrationAvailability(true, null)
                : new IntegrationAvailability(false, "integration_disabled");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Kill-switch check failed for integration {IntegrationKey}/{BroadcasterId}; degrading to disabled rather than throwing.",
                integrationKey,
                broadcasterId
            );
            return new IntegrationAvailability(false, "unavailable");
        }
    }
}
