// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
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
    /// <summary>
    /// The global, seeded billing-tier keys (monetization-billing.md N.1;
    /// <see cref="NomNomzBot.Infrastructure.Content.Billing.BillingTierSeeder"/> is the seed source of truth) — <c>free</c> is the
    /// non-public self-host/unbilled marker, <c>base</c>/<c>pro</c>/<c>premium</c> are the hosted cloud plans.
    /// Global reference data, not tenant-configured, so this is a genuinely closed set (S045b) rather than a
    /// <see cref="PipelineActionFieldKind.ResourceId"/> lookup.
    /// </summary>
    private static readonly string[] TierKeys = ["free", "base", "pro", "premium"];

    public string ActionType => "require_tier";

    public LocalizedText Category => new("pipeline.category.billing");

    public LocalizedText Description => new("pipeline.require_tier.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "min_tier",
                PipelineActionFieldKind.Enum,
                Required: true,
                Options: TierKeys,
                Description: new("pipeline.require_tier.min_tier.help")
            ),
            new(
                "denied_message",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.require_tier.denied_message.help")
            ),
        ];

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
