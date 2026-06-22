// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;

namespace NomNomzBot.Infrastructure.Billing.PipelineActions;

/// <summary>
/// Pipeline action <c>require_tier</c> (monetization-billing.md §6): gates the pipeline on the channel's
/// entitlement so authors can build premium-only commands. Params: <c>min_tier</c> (the required tier key) and
/// optional <c>denied_message</c>. Reads only — never mutates billing, no metering, no events. Fail-closed:
/// below the floor stops the pipeline. Self-host always satisfies (unlimited profile).
/// </summary>
public sealed class RequireTierAction(IBillingTierService tiers) : ICommandAction
{
    public string ActionType => "require_tier";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string minTier = action.GetString("min_tier") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(minTier))
            return ActionResult.Failure("require_tier requires a min_tier.");

        Result<bool> satisfied = await tiers.IsTierAtLeastAsync(
            ctx.BroadcasterId,
            minTier,
            ctx.CancellationToken
        );
        if (satisfied.IsFailure)
            return ActionResult.Failure(satisfied.ErrorMessage ?? "require_tier failed.");
        if (!satisfied.Value)
            return ActionResult.Failure(
                action.GetString("denied_message")
                    ?? $"This command requires the {minTier} tier or higher."
            );

        return ActionResult.Success();
    }
}
