// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Platform;

/// <summary>A feature flag's global definition (admin view).</summary>
public sealed record FeatureFlagDto(
    string Key,
    string? Description,
    bool IsEnabledGlobally,
    int RolloutPercentage,
    string? MinTierKey,
    string? RequiresConsent,
    string? DeploymentMode
);

/// <summary>Create-or-update a flag's global definition (rollout-updates §5 ramp controls).</summary>
public sealed record SetFeatureFlagRequest(
    string Key,
    string? Description,
    bool IsEnabledGlobally,
    int RolloutPercentage,
    string? MinTierKey = null,
    string? RequiresConsent = null,
    string? DeploymentMode = null
);

/// <summary>Set a per-tenant override (the internal/beta opt-in or per-channel kill-switch).</summary>
public sealed record SetFeatureFlagOverrideRequest(
    bool IsEnabled,
    string? Reason = null,
    DateTime? ExpiresAt = null
);
