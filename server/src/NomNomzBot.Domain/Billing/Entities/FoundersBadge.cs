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
/// A founders badge (monetization-billing.md N.6) — a cosmetic perk granted by invite redemption or admin grant.
/// Tenant-scoped but NOT soft-deletable (the perk persists). One per channel.
/// </summary>
public class FoundersBadge : ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public DateTime GrantedAt { get; set; }
    public string? InviteCode { get; set; }
    public bool IsActive { get; set; }
}
