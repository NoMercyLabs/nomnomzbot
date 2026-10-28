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
/// A per-tenant override of a <see cref="FeatureFlag"/> (schema P.13). The highest-precedence input to evaluation:
/// an unexpired override forces the flag on/off for that channel regardless of the global toggle/ramp — the basis
/// for internal/beta opt-in and the per-channel kill-switch (rollout-updates §5).
/// </summary>
public class FeatureFlagOverride : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid FeatureFlagId { get; set; }
    public Guid BroadcasterId { get; set; }
    public bool IsEnabled { get; set; }
    public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
