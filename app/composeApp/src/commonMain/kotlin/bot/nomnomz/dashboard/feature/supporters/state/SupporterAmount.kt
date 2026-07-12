// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.supporters.state

/**
 * Formats a supporter amount from MINOR units (cents) to a display string in MAJOR units, optionally suffixed with
 * the [currency] code (supporter-events.md §5 — `amountMinor` is cents). Returns `null` when [amountMinor] is null
 * (the provider sent no amount) so the caller renders nothing rather than a bogus "0.00".
 *
 * The two-decimal major-unit string is built by hand (no `String.format`, which is JVM-only and absent on Wasm):
 * e.g. `(500, "USD")` → `"5.00 USD"`, `(1234, null)` → `"12.34"`, `(5, "EUR")` → `"0.05 EUR"`, `(-250, "USD")` →
 * `"-2.50 USD"` (a refund/chargeback keeps its sign).
 */
fun formatSupporterAmount(amountMinor: Long?, currency: String?): String? {
    if (amountMinor == null) return null

    val negative: Boolean = amountMinor < 0
    val absMinor: Long = if (negative) -amountMinor else amountMinor
    val whole: Long = absMinor / 100
    val cents: Long = absMinor % 100
    val centsText: String = if (cents < 10) "0$cents" else "$cents"
    val sign: String = if (negative) "-" else ""
    val major: String = "$sign$whole.$centsText"

    val code: String? = currency?.takeIf { it.isNotBlank() }
    return if (code != null) "$major $code" else major
}
