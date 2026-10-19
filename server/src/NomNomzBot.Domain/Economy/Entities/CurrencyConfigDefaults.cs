// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// The one place each currency setting default is written. A channel with no saved currency row resolves to
/// these values; the entity and the API both read them.
/// </summary>
public static class CurrencyConfigDefaults
{
    public const string CurrencyName = "points";
    public const string CurrencyNamePlural = "points";
    public static readonly string? IconUrl = null;
    public const bool IsEnabled = false;
    public const long StartingBalance = 0;
    public static readonly long? MaxBalance = null;
    public const int DecimalPlaces = 0;

    /// <summary>The most decimal places a currency may show.</summary>
    public const int MaxDecimalPlaces = 8;
}
