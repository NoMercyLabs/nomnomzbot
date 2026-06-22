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
