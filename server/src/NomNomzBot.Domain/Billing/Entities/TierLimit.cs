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
/// One quota lever for a tier (monetization-billing.md N.2) — GLOBAL. <c>LimitValue = -1</c> means unlimited
/// (self-host resolves every limit to unlimited). One per <c>(TierId, LimitKey)</c>.
/// </summary>
public class TierLimit : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TierId { get; set; }
    public string LimitKey { get; set; } = null!;
    public long LimitValue { get; set; }
}
