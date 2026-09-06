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
/// The owner-authored real-world price of one usage unit (S-ADMIN-4d) — GLOBAL reference data, not
/// tenant-scoped, keyed by <see cref="UnitKey"/>: the SAME string used as <c>UsageRecord.MetricKey</c>
/// (e.g. <c>sandbox_exec_ms</c>), or the synthetic <see cref="PricedUnitKeys.TtsCharacters"/> key for the
/// TTS character counter, which is metered separately in <c>TtsUsageRecord</c>.
/// <para>
/// Money is integer minor units, never a float: <see cref="PriceMinorUnitsPerBatch"/> is charged per
/// <see cref="BatchSize"/> raw units. A <see cref="BatchSize"/> greater than 1 lets a real sub-minor-unit
/// cost (a fraction of a cent per TTS character, per millisecond of sandbox CPU) be represented exactly in
/// integers — e.g. 5 minor units per 1,000 characters — rather than rounding to whole cents per unit or
/// resorting to a float.
/// </para>
/// <para>
/// <b>No row for a <see cref="UnitKey"/> means that unit is UNPRICED</b> — usage still reports its measured
/// quantity, but computing a cost for it is refused rather than assumed to be zero. "Not priced yet" and
/// "priced at zero" are different claims; only a row with <see cref="PriceMinorUnitsPerBatch"/> explicitly
/// set to 0 makes the second one true.
/// </para>
/// <para>
/// <b>Price-change semantics (deliberate choice):</b> there is no per-period price snapshot or cost ledger
/// in this codebase. Cost is always computed live as <c>quantity * PriceMinorUnitsPerBatch / BatchSize</c>
/// against whichever <see cref="PricedUnit"/> row exists RIGHT NOW for a key — i.e. usage is repriced at the
/// CURRENT rate every time it is queried, including usage recorded before the price last changed. This
/// keeps the model to a single authored price per unit (matching <c>BillingTier.PriceCents</c>'s own
/// current-rate-only shape) instead of adding effective-dated price history, which nothing in this slice
/// needs yet — a genuine invoice/ledger feature would price historic usage at the rate in effect when it
/// happened, and should introduce that history table when it exists.
/// </para>
/// </summary>
public class PricedUnit : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string UnitKey { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public long PriceMinorUnitsPerBatch { get; set; }
    public long BatchSize { get; set; } = 1;
}

/// <summary>Well-known <see cref="PricedUnit.UnitKey"/> values that do not come straight off a
/// <c>UsageRecord.MetricKey</c> string.</summary>
public static class PricedUnitKeys
{
    /// <summary>Prices the TTS character counter (<c>TtsUsageRecord.CharacterCount</c>), which is metered in
    /// its own table rather than through <c>UsageRecord</c>.</summary>
    public const string TtsCharacters = "tts_characters";
}
