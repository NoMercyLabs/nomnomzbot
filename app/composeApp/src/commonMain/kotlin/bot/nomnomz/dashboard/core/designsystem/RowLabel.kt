// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem

/**
 * Resolves the primary label shown on a list/table row.
 *
 * A row's primary label must NEVER render as an empty string — a nameless row that still carries
 * destructive actions (delete/disable) lets a user act on something they cannot identify. This is
 * the single shared mechanism for every row-label call site in the dashboard: prefer the item's own
 * meaningful name, fall back to a secondary identity (a trigger, a kind, a login), and only as a last
 * resort fall back to a typed, discriminating placeholder — never a bare "Untitled" and never a raw
 * id/ULID/snowflake as the visible text.
 *
 * @param primary the item's own name/title/label/displayName field — may be null or blank.
 * @param secondary a meaningful secondary identity (trigger, kind, login, etc.) — may be null or blank.
 * @param typeLabel a human word for what kind of thing this is (e.g. "Command", "Widget", "Reward").
 * @param discriminatorSource any stable per-item value (its id is fine here — it is never rendered
 *   raw, only hashed into a short code) used to make two blank-named items resolve to different labels.
 */
fun resolveRowLabel(
    primary: String?,
    secondary: String? = null,
    typeLabel: String,
    discriminatorSource: String,
): String {
    if (!primary.isNullOrBlank()) return primary
    if (!secondary.isNullOrBlank()) return secondary
    return "$typeLabel #${discriminatorCode(discriminatorSource)}"
}

/**
 * Derives a short, deterministic, non-reversible discriminator code from an id/timestamp/any stable
 * string — used only to tell two otherwise-identical fallback labels apart. Never render [source]
 * itself as a label; render this code instead.
 */
private fun discriminatorCode(source: String): String {
    val hash: Int = source.hashCode()
    return hash.toUInt().toString(36).uppercase().takeLast(4).padStart(4, '0')
}
