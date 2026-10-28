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

namespace NomNomzBot.Application.Abstractions.Platform;

/// <summary>
/// Read-only feature-flag evaluation (platform-conventions §3.4). A feature's owning service calls this at its
/// entry point to gate the feature; disabled → return FEATURE_DISABLED or skip. Effective state follows the
/// staged-rollout precedence (rollout-updates §5): an unexpired tenant override wins, else the global toggle gated
/// by a deterministic rollout-% bucket and the deployment-mode gate. Results are cached and bounded by a short TTL.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>Evaluate the flag for the current tenant (resolved from <c>ICurrentTenantService</c>).</summary>
    Task<bool> IsEnabledAsync(string flagKey, CancellationToken ct = default);

    /// <summary>Evaluate the flag for an explicit channel (for background workers with no ambient tenant).</summary>
    Task<bool> IsEnabledForAsync(
        string flagKey,
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Full evaluation for gating UIs: whether a flag is even DEFINED for the key, its effective enabled state,
    /// and — when a defined flag is disabled — the blocking reason and the tier floor (for an upgrade prompt).
    /// Callers that only need the boolean use <see cref="IsEnabledForAsync"/>; this is for surfaces that must
    /// tell "no gate governs this" apart from "a gate blocks it", and explain WHY.
    /// </summary>
    Task<FeatureFlagEvaluation> EvaluateAsync(
        string flagKey,
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
