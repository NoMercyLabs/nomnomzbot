// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

    /// <summary>Set (upsert) a per-tenant override; invalidates that channel's cached evaluation.</summary>
    Task<Result> SetOverrideAsync(
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
}
