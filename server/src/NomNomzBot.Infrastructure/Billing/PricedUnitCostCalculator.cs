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

using NomNomzBot.Domain.Billing.Entities;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Pure arithmetic joining a measured usage quantity against an owner-authored <see cref="PricedUnit"/>
/// (S-ADMIN-4d). Money in, money out: everything is an integer minor-unit amount, never a float.
/// </summary>
public static class PricedUnitCostCalculator
{
    /// <summary>
    /// <c>quantity * price.PriceMinorUnitsPerBatch / price.BatchSize</c>, truncated toward zero by integer
    /// division. A <see cref="PricedUnit.BatchSize"/> greater than 1 is what lets a real sub-minor-unit cost
    /// per raw unit be priced exactly in integers; the truncation only ever discards a fractional minor
    /// unit smaller than the smallest real currency amount, so the platform is never shown owing MORE than
    /// the priced rate actually implies.
    /// </summary>
    public static long ComputeCostMinorUnits(long quantity, PricedUnit price) =>
        quantity * price.PriceMinorUnitsPerBatch / price.BatchSize;
}
