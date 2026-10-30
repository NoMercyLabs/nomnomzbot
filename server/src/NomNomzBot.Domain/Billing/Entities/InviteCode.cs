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
/// A redeemable invite code (monetization-billing.md N.7) — GLOBAL. May grant a founders badge and/or a tier,
/// bounded by <c>MaxRedemptions</c> and an optional expiry. A granted tier comes as a gift (S-INVITE-TIER) that
/// lasts <see cref="GrantsMonths"/> months from the redemption, or forever when <see cref="GrantsMonths"/> is
/// null (so older rows keep their meaning). A length without a tier is invalid.
/// </summary>
public class InviteCode : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = null!;
    public int MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }
    public bool GrantsFoundersBadge { get; set; }
    public Guid? GrantsTierId { get; set; }
    public int? GrantsMonths { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
