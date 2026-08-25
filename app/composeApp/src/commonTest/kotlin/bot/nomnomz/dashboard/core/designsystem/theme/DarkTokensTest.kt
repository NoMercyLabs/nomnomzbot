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

import androidx.compose.ui.graphics.Color
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue
import androidx.compose.ui.unit.dp

class DarkTokensTest {

    @Test
    fun dark_surfaces_keep_the_intended_depth_order() {
        assertTrue(relativeLuminance(DarkTokens.sidebar) < relativeLuminance(DarkTokens.background))
        assertTrue(relativeLuminance(DarkTokens.background) < relativeLuminance(DarkTokens.card))
        assertTrue(relativeLuminance(DarkTokens.card) < relativeLuminance(DarkTokens.muted))
    }

    @Test
    fun dark_content_pairs_remain_readable() {
        assertContrastAtLeast(DarkTokens.foreground, DarkTokens.background, 7.0)
        assertContrastAtLeast(DarkTokens.cardForeground, DarkTokens.card, 7.0)
        assertContrastAtLeast(DarkTokens.primaryForeground, DarkTokens.primary, 4.5)
        assertContrastAtLeast(DarkTokens.secondaryForeground, DarkTokens.secondary, 4.5)
        assertContrastAtLeast(DarkTokens.mutedForeground, DarkTokens.muted, 4.5)
        assertContrastAtLeast(DarkTokens.destructiveForeground, DarkTokens.destructive, 4.5)
    }

    @Test
    fun dark_focus_ring_is_visible_on_the_canvas() {
        assertContrastAtLeast(DarkTokens.ring, DarkTokens.background, 3.0)
    }

    @Test
    fun dark_control_geometry_is_expressed_by_shared_tokens() {
        assertEquals(16.dp, DarkTokens.radius.sm)
        assertEquals(16.dp, DarkTokens.radius.md)
        assertEquals(20.dp, DarkTokens.radius.lg)
        assertEquals(24.dp, DarkTokens.radius.xl)
        assertEquals(10.dp, DefaultSpacing.s2_5)
    }

    @Test
    fun user_color_drives_the_complete_accent_family() {
        val accented: Tokens = DarkTokens.withAccent("#07884F")

        assertNotEquals(DarkTokens.primary, accented.primary)
        assertNotEquals(DarkTokens.secondary, accented.secondary)
        assertNotEquals(DarkTokens.accent, accented.accent)
        assertEquals(accented.primary, accented.ring)
        assertEquals(accented.primary, accented.sidebarRing)
        assertContrastAtLeast(accented.primaryForeground, accented.primary, 4.5)
        assertTrue(accented.sidebarPrimaryForeground.red > 0.95f)
        assertTrue(accented.sidebarPrimaryForeground.green > 0.95f)
        assertTrue(accented.sidebarPrimaryForeground.blue > 0.95f)
        assertContrastAtLeast(accented.sidebarPrimaryForeground, accented.sidebarPrimary, 4.5)
    }

    @Test
    fun low_contrast_user_color_is_corrected_without_falling_back_to_ube() {
        val accented: Tokens = DarkTokens.withAccent("#111111")

        assertNotEquals(DarkTokens.primary, accented.primary)
        assertContrastAtLeast(accented.primary, accented.background, 4.5)
        assertContrastAtLeast(accented.primaryForeground, accented.primary, 4.5)
    }

    private fun assertContrastAtLeast(foreground: Color, background: Color, minimum: Double) {
        val foregroundLuminance: Double = relativeLuminance(foreground)
        val backgroundLuminance: Double = relativeLuminance(background)
        val contrast: Double =
            (max(foregroundLuminance, backgroundLuminance) + 0.05) /
                (min(foregroundLuminance, backgroundLuminance) + 0.05)
        assertTrue(contrast >= minimum, "Expected contrast >= $minimum, actual $contrast")
    }

    private fun relativeLuminance(color: Color): Double =
        0.2126 * linearize(color.red) +
            0.7152 * linearize(color.green) +
            0.0722 * linearize(color.blue)

    private fun linearize(channel: Float): Double {
        val value: Double = channel.toDouble()
        return if (value <= 0.04045) value / 12.92 else ((value + 0.055) / 1.055).pow(2.4)
    }
}
