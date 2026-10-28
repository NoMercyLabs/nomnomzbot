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
/// A comp — an operator-issued, time-boxed elevation of one tenant's effective tier (S-ADMIN-4b), distinct
/// from <see cref="TenantLimitOverride"/> (a numeric quota exception for one <c>LimitKey</c>). A LIVE grant
/// (<c>DeletedAt == null</c> and <see cref="ExpiresAt"/> in the future) makes
/// <c>BillingTierService.ResolveTierAsync</c> treat the tenant as being on <see cref="GrantedTierId"/> whenever
/// that ranks above its billed/subscribed tier — so <c>IsTierAtLeastAsync</c>, <c>GetEntitlementAsync</c> and
/// every tier-gated capability that reads them (e.g. the <c>require_tier</c> pipeline action) see the comped
/// tier for as long as the grant is live. Every grant carries a mandatory <see cref="Reason"/> and a mandatory
/// <see cref="ExpiresAt"/> — there is no indefinite comp; a support case that needs to persist is reissued.
/// </summary>
public class EntitlementGrant : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid GrantedTierId { get; set; }
    public string Reason { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime IssuedAt { get; set; }
    public Guid? IssuedByAdminId { get; set; }
}
