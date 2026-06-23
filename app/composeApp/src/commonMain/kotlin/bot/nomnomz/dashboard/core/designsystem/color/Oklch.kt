// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.color

import androidx.compose.ui.graphics.Color
import kotlin.math.cos
import kotlin.math.pow
import kotlin.math.sin

// OKLCH -> sRGB conversion (frontend-design-system.md §1.2). OKLCH is not a native
// Compose color space, so this pure util converts once at theme build. The pipeline
// is pinned so two implementers produce identical colors:
//   * Bjorn Ottosson's published OKLab <-> linear-sRGB matrices (M1/M2 constants),
//   * the standard sRGB piecewise transfer function (not a 2.2 gamma approximation),
//   * an out-of-gamut rule (reduce chroma toward 0 by bisection until in gamut),
//   * 8-bit channel rounding.
//
// Inputs: L in [0,1], C >= 0, H in degrees, A (alpha) in [0,1].

private const val GAMUT_EPSILON: Double = 1e-4

/** Convert an OKLCH color to a Compose [Color] (sRGB). */
internal fun oklch(l: Double, c: Double, h: Double, a: Double = 1.0): Color {
    // Bisection on chroma to bring the color into the sRGB gamut.
    var lo = 0.0
    var hi = c
    var inGamutChroma: Double = if (linearInGamut(l, c, h)) c else 0.0

    if (!linearInGamut(l, c, h)) {
        repeat(40) {
            val mid: Double = (lo + hi) / 2.0
            if (linearInGamut(l, mid, h)) {
                inGamutChroma = mid
                lo = mid
            } else {
                hi = mid
            }
            if (hi - lo < GAMUT_EPSILON) return@repeat
        }
    }

    val linear: Triple<Double, Double, Double> = oklchToLinearSrgb(l, inGamutChroma, h)
    val r: Float = encodeSrgb(linear.first)
    val g: Float = encodeSrgb(linear.second)
    val b: Float = encodeSrgb(linear.third)
    return Color(red = r, green = g, blue = b, alpha = a.toFloat())
}

private fun linearInGamut(l: Double, c: Double, h: Double): Boolean {
    val linear: Triple<Double, Double, Double> = oklchToLinearSrgb(l, c, h)
    return linear.first in 0.0..1.0 && linear.second in 0.0..1.0 && linear.third in 0.0..1.0
}

// OKLCH -> OKLab -> linear sRGB (Ottosson's M1/M2 matrices).
private fun oklchToLinearSrgb(l: Double, c: Double, hDegrees: Double): Triple<Double, Double, Double> {
    val hRadians: Double = hDegrees * (kotlin.math.PI / 180.0)
    val labA: Double = c * cos(hRadians)
    val labB: Double = c * sin(hRadians)

    // OKLab -> LMS' (inverse M2).
    val lPrime: Double = l + 0.3963377774 * labA + 0.2158037573 * labB
    val mPrime: Double = l - 0.1055613458 * labA - 0.0638541728 * labB
    val sPrime: Double = l - 0.0894841775 * labA - 1.2914855480 * labB

    // Undo the cube-root nonlinearity.
    val lCube: Double = lPrime * lPrime * lPrime
    val mCube: Double = mPrime * mPrime * mPrime
    val sCube: Double = sPrime * sPrime * sPrime

    // LMS -> linear sRGB (inverse M1).
    val r: Double = 4.0767416621 * lCube - 3.3077115913 * mCube + 0.2309699292 * sCube
    val g: Double = -1.2684380046 * lCube + 2.6097574011 * mCube - 0.3413193965 * sCube
    val b: Double = -0.0041960863 * lCube - 0.7034186147 * mCube + 1.7076147010 * sCube
    return Triple(r, g, b)
}

// Standard sRGB piecewise transfer function (linear -> gamma-encoded), then clamp.
private fun encodeSrgb(linear: Double): Float {
    val clamped: Double = linear.coerceIn(0.0, 1.0)
    val encoded: Double =
        if (clamped <= 0.0031308) 12.92 * clamped
        else 1.055 * clamped.pow(1.0 / 2.4) - 0.055
    // 8-bit channel rounding.
    val byteValue: Int = (encoded * 255.0 + 0.5).toInt().coerceIn(0, 255)
    return byteValue / 255f
}
