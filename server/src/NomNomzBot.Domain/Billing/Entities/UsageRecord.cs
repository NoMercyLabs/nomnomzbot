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
