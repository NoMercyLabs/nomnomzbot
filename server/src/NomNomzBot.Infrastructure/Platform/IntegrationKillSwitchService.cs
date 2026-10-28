// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
