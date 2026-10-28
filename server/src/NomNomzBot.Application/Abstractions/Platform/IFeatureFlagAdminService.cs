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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Abstractions.Platform;

/// <summary>
/// The feature-flag admin write surface (rollout-updates §5, owned by stream-admin / Plane C). Drives the staged
/// rollout: set the global definition + ramp, and set/clear per-tenant overrides. Every write emits
/// FeatureFlagAdministeredEvent (audit) + FeatureFlagChangedEvent (cache invalidation).
/// </summary>
public interface IFeatureFlagAdminService
{
    Task<Result<IReadOnlyList<FeatureFlagDto>>> ListAsync(CancellationToken ct = default);

    /// <summary>Create or update a flag's global definition (matched by Key).</summary>
    Task<Result<FeatureFlagDto>> SetFlagAsync(
        SetFeatureFlagRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    );

    /// <summary>Every per-tenant override on every flag, by flag key then channel name — the console's read-back.</summary>
    Task<Result<IReadOnlyList<FeatureFlagOverrideDto>>> ListOverridesAsync(
        CancellationToken ct = default
    );

    /// <summary>
    /// Set (upsert) a per-tenant override for an existing channel; invalidates that channel's cached
    /// evaluation and returns the override read back. An unknown channel is refused, never silently stored.
    /// </summary>
    Task<Result<FeatureFlagOverrideDto>> SetOverrideAsync(
        string flagKey,
        Guid broadcasterId,
        SetFeatureFlagOverrideRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    );

    /// <summary>Remove a per-tenant override; invalidates that channel's cached evaluation.</summary>
    Task<Result> RemoveOverrideAsync(
        string flagKey,
        Guid broadcasterId,
        Guid? actorUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Count the active channels that would actually feel a flip of this flag's GLOBAL toggle (consequences
    /// must be visible before a kill-switch commits) — every active, non-overridden channel; an unexpired
    /// per-tenant override is unaffected either way.
    /// </summary>
    Task<Result<FeatureFlagBlastRadiusDto>> PreviewGlobalToggleAsync(
        string flagKey,
        CancellationToken ct = default
    );
}
