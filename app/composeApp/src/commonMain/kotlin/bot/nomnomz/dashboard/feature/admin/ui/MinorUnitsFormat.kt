// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

/**
 * Renders an integer minor-unit amount as its major-unit decimal, e.g. 250 -> "2.50", 1905 -> "19.05",
 * 7 -> "0.07". The API states every cost in minor units; a page that printed the raw number claimed a
 * 2.50 EUR bill was 250 EUR. Padding is done here because the compose-resources formatter ignores
 * width flags on positional specifiers.
 */
internal fun formatMinorUnits(minorUnits: Long): String {
    val sign: String = if (minorUnits < 0) "-" else ""
    val magnitude: Long = if (minorUnits < 0) -minorUnits else minorUnits
    val minor: String = (magnitude % 100).toString().padStart(2, '0')
    return "$sign${magnitude / 100}.$minor"
}
