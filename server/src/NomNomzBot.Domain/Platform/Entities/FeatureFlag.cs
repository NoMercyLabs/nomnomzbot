// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Entities;

/// <summary>
/// A staged-rollout feature flag (schema P.13, GLOBAL — no tenant). Effective state is evaluated (rollout-updates
/// §5): a tenant override wins, else the global toggle gated by a deterministic rollout-% bucket, a minimum tier,
/// a deployment-mode gate, and a consent gate. Carries no <c>BroadcasterId</c>; per-tenant exceptions live on
/// <see cref="FeatureFlagOverride"/>.
/// </summary>
public class FeatureFlag : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Key { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsEnabledGlobally { get; set; }

    /// <summary>0–100; a channel is in the ramp when its stable <c>hash(BroadcasterId, Key)</c> bucket is below this.</summary>
    public int RolloutPercentage { get; set; }

    /// <summary>Minimum billing tier (FK'd source of truth — a renamed tier key can't silently orphan the flag).</summary>
    public Guid? MinTierId { get; set; }

    /// <summary>Denormalized convenience copy of the min tier's key.</summary>
    public string? MinTierKey { get; set; }

    /// <summary>A consent type the tenant must hold for the flag to apply, or null.</summary>
    public string? RequiresConsent { get; set; }

    /// <summary><c>saas</c> | <c>self_host</c> | null = both.</summary>
    public string? DeploymentMode { get; set; }
}
