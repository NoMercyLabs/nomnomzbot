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

import java.io.File
import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue
import kotlin.test.fail
import org.jetbrains.skia.FontMgr
import org.jetbrains.skia.Typeface

/**
 * Owner punch list §10 (first half): the app's whole glyph set on Skia/Wasm is whatever is bundled in
 * `composeResources/font/` — there are no system fonts to fall back to. [appTypography] (Typography.kt)
 * now lists five Noto Sans faces alongside Inter so scripts Inter doesn't cover fall back to a real
 * glyph instead of a tofu box. This test loads the *exact bundled files* through Skia — the same
 * rendering library both the desktop and Wasm targets use (Compose Multiplatform's Wasm target renders
 * through Skia/Skiko too, not the browser's own text engine) — and queries each face's cmap directly,
 * so a claim like "Noto Sans SC covers Han" is proven against the actual shipped bytes, not assumed
 * from the font's name.
 *
 * This proves the bundled *files* carry the needed glyphs. It does not prove Compose's runtime
 * per-glyph cascade picks the right face while shaping a live paragraph on screen — see
 * [AppTypographyFallbackWiringTest] (commonTest) for what's proven about the wiring path, and its
 * doc comment for why full rendering correctness needs an on-screen check this suite doesn't have.
 */
class NotoFallbackGlyphCoverageTest {

    // Codepoint 0 (glyph id, not Unicode) is Skia/OpenType's reserved .notdef glyph — what actually
    // paints as a tofu box. getUTF32Glyph returns it for any codepoint the face has no mapping for.
    private val missingGlyphId: Short = 0

    private fun loadTypeface(fileName: String): Typeface {
        val file: File = File(fontDir(), fileName)
        assertTrue(file.isFile, "expected bundled font at ${file.path}")
        return FontMgr.default.makeFromFile(file.absolutePath)
            ?: fail("Skia could not load $fileName as a typeface")
    }

    private fun fontDir(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, "app/composeApp/src/commonMain/composeResources/font")
            if (candidate.isDirectory) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate composeResources/font from ${System.getProperty("user.dir")}")
    }

    @Test
    fun interAloneDoesNotCoverThai() {
        // Establishes the actual bug being fixed: without a fallback face, this codepoint has no
        // glyph anywhere in the app's bundled font set. If this ever starts passing, Inter itself
        // changed and the Thai fallback below may have become redundant (not wrong, just worth
        // noticing).
        //
        // CORRECTED 2026-09-09: this test originally used Cyrillic Ж (U+0416) as the negative
        // control, on the (wrong, unverified) assumption that Inter lacks it. Verified directly
        // against the bundled inter.ttf's cmap (fontTools): Inter DOES cover Cyrillic — the modern
        // Inter release bundled here includes it. Cyrillic coverage is still proven separately in
        // [notoSansCoversCyrillicGreekVietnameseAndDevanagari]; it is just redundant with Inter now,
        // not a real gap. Thai is verified (same cmap check) to be genuinely absent from Inter.
        val inter: Typeface = loadTypeface("inter.ttf")
        val thaiKo: Int = 0x0E01 // ก
        assertTrue(
            inter.getUTF32Glyph(thaiKo) == missingGlyphId,
            "expected Inter to lack a Thai glyph (proving the fallback is needed), but it has one",
        )
    }

    @Test
    fun notoSansCoversCyrillicGreekVietnameseAndDevanagari() {
        val notoSans: Typeface = loadTypeface("noto_sans.ttf")
        val cyrillicZhe: Int = 0x0416 // Ж
        val greekAlpha: Int = 0x03B1 // α
        val vietnameseATilde: Int = 0x1EA1 // ạ
        val devanagariA: Int = 0x0905 // अ
        assertFalse(notoSans.getUTF32Glyph(cyrillicZhe) == missingGlyphId, "Noto Sans should cover Cyrillic Ж (U+0416)")
        assertFalse(notoSans.getUTF32Glyph(greekAlpha) == missingGlyphId, "Noto Sans should cover Greek α (U+03B1)")
        assertFalse(
            notoSans.getUTF32Glyph(vietnameseATilde) == missingGlyphId,
            "Noto Sans should cover Vietnamese ạ (U+1EA1)",
        )
        assertFalse(notoSans.getUTF32Glyph(devanagariA) == missingGlyphId, "Noto Sans should cover Devanagari अ (U+0905)")
    }

    @Test
    fun notoSansArabicCoversArabicScript() {
        val notoSansArabic: Typeface = loadTypeface("noto_sans_arabic.ttf")
        val arabicAlef: Int = 0x0627 // ا
        assertFalse(
            notoSansArabic.getUTF32Glyph(arabicAlef) == missingGlyphId,
            "Noto Sans Arabic should cover Arabic ا (U+0627)",
        )
    }

    @Test
    fun notoSansThaiCoversThaiScript() {
        val notoSansThai: Typeface = loadTypeface("noto_sans_thai.ttf")
        val thaiKoKai: Int = 0x0E01 // ก
        assertFalse(notoSansThai.getUTF32Glyph(thaiKoKai) == missingGlyphId, "Noto Sans Thai should cover Thai ก (U+0E01)")
    }

    @Test
    fun notoSansScCoversHanHiraganaKatakanaAndBopomofo() {
        val notoSansSc: Typeface = loadTypeface("noto_sans_sc.ttf")
        val han: Int = 0x4E2D // 中
        val hiragana: Int = 0x3042 // あ
        val katakana: Int = 0x30A2 // ア
        val bopomofo: Int = 0x3105 // ㄅ
        assertFalse(notoSansSc.getUTF32Glyph(han) == missingGlyphId, "Noto Sans SC should cover Han 中 (U+4E2D)")
        assertFalse(notoSansSc.getUTF32Glyph(hiragana) == missingGlyphId, "Noto Sans SC should cover Hiragana あ (U+3042)")
        assertFalse(notoSansSc.getUTF32Glyph(katakana) == missingGlyphId, "Noto Sans SC should cover Katakana ア (U+30A2)")
        assertFalse(notoSansSc.getUTF32Glyph(bopomofo) == missingGlyphId, "Noto Sans SC should cover Bopomofo ㄅ (U+3105)")
    }

    @Test
    fun notoSansScDoesNotCoverHangul() {
        // Documents why Noto Sans KR is bundled separately rather than relying on SC for all of "CJK":
        // Han and Hangul are different scripts, and SC's cmap genuinely has no Hangul mapping.
        val notoSansSc: Typeface = loadTypeface("noto_sans_sc.ttf")
        val hangul: Int = 0xD55C // 한
        assertTrue(
            notoSansSc.getUTF32Glyph(hangul) == missingGlyphId,
            "expected Noto Sans SC to lack Hangul (proving Noto Sans KR is a separate, necessary face)",
        )
    }

    @Test
    fun notoSansKrCoversHangul() {
        val notoSansKr: Typeface = loadTypeface("noto_sans_kr.ttf")
        val hangulSyllable: Int = 0xD55C // 한
        val hangulJamo: Int = 0x1100 // ᄀ
        assertFalse(notoSansKr.getUTF32Glyph(hangulSyllable) == missingGlyphId, "Noto Sans KR should cover Hangul 한 (U+D55C)")
        assertFalse(notoSansKr.getUTF32Glyph(hangulJamo) == missingGlyphId, "Noto Sans KR should cover Hangul Jamo (U+1100)")
    }
}
