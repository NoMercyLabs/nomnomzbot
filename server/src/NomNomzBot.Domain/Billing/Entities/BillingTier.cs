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
/// A billing plan (monetization-billing.md N.1) — GLOBAL reference data, not tenant-scoped. The hosted cloud
/// plans are <c>base</c>/<c>pro</c>/<c>premium</c> (<c>IsPublic</c>); <c>free</c> is the internal self-host /
/// unbilled marker only (never public). <c>AllowsCustomBotName</c> is true for pro+. Money is integer cents.
/// </summary>
public class BillingTier : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public int PriceCents { get; set; }
    public string Currency { get; set; } = null!;
    public string? StripePriceId { get; set; }
    public string? StripeProductId { get; set; }
    public bool AllowsCustomBotName { get; set; }
    public bool PrioritySupport { get; set; }
    public bool IsPublic { get; set; }
    public int SortOrder { get; set; }
}
