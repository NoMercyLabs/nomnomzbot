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

/// <summary>
/// The counted blast radius of flipping a flag's GLOBAL toggle — shown to the operator BEFORE the toggle
/// commits (consequences-must-be-visible). <see cref="TenantsAffected"/> is the number of active channels
/// whose effective state is actually governed by the global toggle right now: an unexpired per-tenant
/// override insulates a channel from the global switch, so an overridden channel is never counted here — it
/// will not change. This is the same reasoning that makes a per-integration kill switch safe to flip: the
/// operator sees exactly how many tenants lose the integration, not an estimate.
/// </summary>
/// <param name="TenantsAffected">Real, counted number of active channels with no unexpired override for this flag.</param>
/// <param name="SampleChannelNames">Up to 5 channel names from that set, so the operator recognises who is affected.</param>
public sealed record FeatureFlagBlastRadiusDto(
    int TenantsAffected,
    IReadOnlyList<string> SampleChannelNames
);
