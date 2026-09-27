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

import kotlin.test.Test
import kotlin.test.assertEquals

class MinorUnitsFormatTest {

    @Test
    fun minor_units_render_as_a_two_decimal_major_amount() {
        assertEquals("2.50", formatMinorUnits(250))
        assertEquals("19.05", formatMinorUnits(1905))
        assertEquals("0.07", formatMinorUnits(7))
        assertEquals("0.00", formatMinorUnits(0))
        assertEquals("-1.20", formatMinorUnits(-120))
    }
}
