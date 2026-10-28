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

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// A channel's single currency definition (economy.md K.1) — its name, starting/max balance, and whether the
/// economy is enabled. One per channel (<c>Unique(BroadcasterId)</c>).
/// </summary>
public class CurrencyConfig : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public string CurrencyName { get; set; } = CurrencyConfigDefaults.CurrencyName;
    public string? CurrencyNamePlural { get; set; } = CurrencyConfigDefaults.CurrencyNamePlural;
    public string? IconUrl { get; set; } = CurrencyConfigDefaults.IconUrl;
    public bool IsEnabled { get; set; } = CurrencyConfigDefaults.IsEnabled;
    public long StartingBalance { get; set; } = CurrencyConfigDefaults.StartingBalance;
    public long? MaxBalance { get; set; } = CurrencyConfigDefaults.MaxBalance;
    public int DecimalPlaces { get; set; } = CurrencyConfigDefaults.DecimalPlaces;
}
