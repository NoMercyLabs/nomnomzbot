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
/// A metered usage counter for one channel + metric + period (monetization-billing.md N.5). APPEND-ONLY: a plain
/// class with a <c>long</c> identity and <c>CreatedAt</c>, tenant-scoped (no soft-delete). One per
/// <c>(BroadcasterId, MetricKey, PeriodStart)</c>; <c>MetricKey</c> matches a <c>TierLimit.LimitKey</c>.
/// </summary>
public class UsageRecord : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public string MetricKey { get; set; } = null!;
    public long Quantity { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public bool ReportedToStripe { get; set; }
    public DateTime CreatedAt { get; set; }
}
