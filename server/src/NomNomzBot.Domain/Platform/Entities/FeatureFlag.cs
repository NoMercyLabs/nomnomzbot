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
