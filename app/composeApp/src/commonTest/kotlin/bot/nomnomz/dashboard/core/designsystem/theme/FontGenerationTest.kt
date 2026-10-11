// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.theme

import androidx.compose.ui.unit.Density
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

class FontGenerationTest {
    private val base: Density = Density(density = 2f, fontScale = 1f)

    @Test
    fun noLoadedFaceLeavesTheDensityUntouched() {
        assertEquals(base, base.withFontGeneration(0))
    }

    @Test
    fun everyLoadedFaceChangesTheLayoutInputSoVisibleTextIsLaidOutAgain() {
        val seen: MutableSet<Float> = mutableSetOf(base.fontScale)
        for (generation: Int in 1..9) {
            val next: Density = base.withFontGeneration(generation)
            assertTrue(seen.add(next.fontScale), "generation $generation reused an earlier font scale")
            assertEquals(base.density, next.density)
        }
        assertNotEquals(base, base.withFontGeneration(1))
    }

    @Test
    fun theNudgeStaysBelowWhatAReaderCouldSee() {
        val worst: Density = base.withFontGeneration(9)
        assertTrue(worst.fontScale / base.fontScale < 1.002f, "font scale moved to ${worst.fontScale}")
    }
}
