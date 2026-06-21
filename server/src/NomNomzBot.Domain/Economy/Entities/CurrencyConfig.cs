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

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// A channel's single currency definition (economy.md K.1) — its name, starting/max balance, and whether the
/// economy is enabled. One per channel (<c>Unique(BroadcasterId)</c>).
/// </summary>
public class CurrencyConfig : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public string CurrencyName { get; set; } = null!;
    public string? CurrencyNamePlural { get; set; }
    public string? IconUrl { get; set; }
    public bool IsEnabled { get; set; }
    public long StartingBalance { get; set; }
    public long? MaxBalance { get; set; }
    public int DecimalPlaces { get; set; }
}
