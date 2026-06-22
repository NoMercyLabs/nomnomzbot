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
