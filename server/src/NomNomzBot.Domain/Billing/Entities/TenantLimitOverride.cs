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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Billing.Entities;

/// <summary>
/// A per-tenant quota exception (S-ADMIN-3) — an operator-set ceiling for one <c>(BroadcasterId,
/// LimitKey)</c> pair that overrides both the NEAR_FREE safety baseline and the tier-resolved COST_DRIVING
/// limit for that tenant alone. <c>LimitValue = -1</c> means unlimited. Distinct from <see cref="TierLimit"/>,
/// which is GLOBAL per tier — this row exists for the one operator-granted exception (a support case, a
/// negotiated deal, a temporary abuse-response tightening), never as a way to reconfigure a tier itself.
/// An expired or soft-deleted row stops applying. Read through <c>LiveLimitOverrides</c> by entitlement
/// resolution and the quota check, so every enforcer of a limit sees the override.
/// </summary>
public class TenantLimitOverride : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public string LimitKey { get; set; } = null!;
    public long LimitValue { get; set; }
    public string Reason { get; set; } = null!;
    public Guid GrantedByPrincipalId { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
