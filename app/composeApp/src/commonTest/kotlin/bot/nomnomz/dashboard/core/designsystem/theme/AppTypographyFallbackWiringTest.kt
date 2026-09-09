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

import androidx.compose.material3.Text
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.text.font.FontListFontFamily
import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * Owner punch list §10 (first half): Inter alone is the app's entire glyph set on Skia/Wasm (no system
 * fonts to fall back to), so a script Inter doesn't cover renders as tofu. [appTypography] now bundles
 * five Noto Sans fallback faces (Cyrillic/Greek/Vietnamese/Devanagari, Arabic, Thai, Han+Kana, Hangul)
 * alongside Inter and the emoji face, all in one [FontFamily], which is how Compose Multiplatform's
 * font-fallback cascade actually works (confirmed against the same mechanism this codebase already
 * ships for emoji, and against JetBrains' own font-fallback work for Skia/Wasm — a `FontFamily` walks
 * its listed [Font] entries per glyph, not per whole string, so a mixed-script string resolves each
 * character from whichever face in the list actually has it).
 *
 * What this test CAN prove, without rendering to a screen: that [appTypography]'s [FontFamily] really
 * does carry all five fallback faces (not silently dropped or miswired), that it reaches every screen
 * through [NomNomzTheme] → [LocalTypography] the same way Inter does, and that a string mixing six
 * scripts survives the full theme → [Text] → semantics pipeline unmangled (no character substitution,
 * no truncation).
 *
 * What it CANNOT prove: that any given glyph paints as a real shape instead of a tofu box. That is a
 * rasterization outcome — it depends on Skia's per-glyph cascade inside text shaping, which has no
 * public Compose test API to interrogate. [NotoFallbackGlyphCoverageTest] (jvmTest) proves the actual
 * bundled font *files* contain the needed glyphs by loading them through Skia directly and querying
 * their cmaps; only an on-screen render (or a pixel-diff screenshot test, not set up in this project)
 * closes the gap between "the glyph exists in the bundled file" and "Skia chose it while shaping this
 * exact paragraph."
 */
@OptIn(ExperimentalTestApi::class)
class AppTypographyFallbackWiringTest {

    @Test
    fun appTypographyFamilyCarriesInterPlusAllFiveScriptFallbacksPlusEmoji() =
        runComposeUiTest {
            var resolvedFamily: androidx.compose.ui.text.font.FontFamily? = null
            setContent {
                NomNomzTheme {
                    resolvedFamily = LocalTypography.current.base.fontFamily
                }
            }

            val family = resolvedFamily
            val fontList: FontListFontFamily =
                family as? FontListFontFamily
                    ?: error("appTypography() should build a FontListFontFamily (Inter + fallbacks), got $family")

            // 4 Inter weights + Noto Sans + Noto Sans Arabic + Noto Sans Thai + Noto Sans SC + Noto Sans KR
            // + 1 emoji face = 10. A count regression here means a fallback face was dropped from the
            // FontFamily(...) call in appTypography() without anything else catching it.
            assertEquals(
                10,
                fontList.size,
                "appTypography()'s FontFamily should carry Inter (4 weights) + 5 Noto script fallbacks + 1 emoji face",
            )
        }

    @Test
    fun sixScriptStringSurvivesTheThemeAndTextPipelineUnmangled() =
        runComposeUiTest {
            // Latin, Cyrillic, Greek, Han, Hangul, Arabic, Thai — one string per bundled fallback face
            // (plus Latin, which Inter itself covers) so a real mixed-script chat-style line is exercised
            // end to end, not one script in isolation.
            val mixedScriptSample: String = "Hello Привет Γειά 你好 한글 مرحبا สวัสดี"
            setContent {
                NomNomzTheme {
                    Text(text = mixedScriptSample, style = LocalTypography.current.base)
                }
            }

            // Proves the exact string reaches the semantics tree unchanged (no glyph-substitution
            // placeholder character, no dropped script run) — reachability, not pixel correctness.
            onNodeWithText(mixedScriptSample).assertExists()
        }
}
