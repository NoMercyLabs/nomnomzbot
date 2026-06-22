// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Billing.Entities;

/// <summary>
/// A redeemable invite code (monetization-billing.md N.7) — GLOBAL. May grant a founders badge and/or a tier,
/// bounded by <c>MaxRedemptions</c> and an optional expiry.
/// </summary>
public class InviteCode : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = null!;
    public int MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }
    public bool GrantsFoundersBadge { get; set; }
    public Guid? GrantsTierId { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
