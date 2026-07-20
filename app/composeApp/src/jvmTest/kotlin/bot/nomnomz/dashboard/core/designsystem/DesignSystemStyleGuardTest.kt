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

import java.io.File
import kotlin.test.Test
import kotlin.test.fail

// The design-system enforcement CLAUDE.md always claimed ("a detekt linter bans raw hex/dp, off-catalogue
// components") but that was never built — there is no detekt/ktlint anywhere and CI runs no lint. This is that
// gate, as a local jvmTest (no CI needed): feature screens must use theme tokens (LocalTokens) and spacing
// (LocalSpacing), never a hardcoded Color(0x…) or N.dp, and must use the component catalogue, never a raw
// Material3 primitive that has a design-system wrapper.
//
// Raw hex/dp that predate this guard are grandfathered per-file (the drift no gate ever caught) so it is green
// today while BLOCKING ANY NEW violation. The baseline is the burn-down list — lower a number when you tokenize
// a file; never raise one. Off-catalogue component use is already zero, so it is enforced strictly at zero.
class DesignSystemStyleGuardTest {

    // Pre-existing raw hex/dp counts per feature file (path relative to feature/), captured 2026-07-20.
    private val rawStyleBaseline: Map<String, Int> =
        mapOf(
            "chat/ui/ChatScreen.kt" to 17,
            "connect/ui/ProviderBrand.kt" to 9, // provider brand colors (Twitch/Spotify/…) — likely permanent
            "connect/ui/ConnectModal.kt" to 8,
            "connect/ui/ConnectModalGlyphs.kt" to 6,
            "shell/ui/ShellGlyphs.kt" to 4,
            "shell/ui/ShellScreen.kt" to 1,
            "participant/ui/ParticipantShell.kt" to 1,
            "landing/ui/LandingScreen.kt" to 1,
            "economy/ui/EconomyScreen.kt" to 1,
            "chat/ui/EmoteComposerField.kt" to 1,
        )

    private val rawDp: Regex = Regex("""\b\d+\.dp\b""")
    private val rawHex: Regex = Regex("""Color\(0x""")

    // Material3 primitives that have a design-system catalogue wrapper — feature code must use the wrapper.
    private val offCatalogue: Regex =
        Regex(
            "import androidx\\.compose\\.material3\\." +
                "(Button|OutlinedButton|TextButton|Card|TextField|OutlinedTextField|Badge|Checkbox|Switch|Slider|Chip|AssistChip|FilterChip)\\b"
        )

    @Test
    fun feature_screens_use_tokens_not_raw_hex_or_dp() {
        val root: File = featureRoot()
        val offenders: MutableList<String> = mutableListOf()
        root.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            val rel: String = file.relativeTo(root).path.replace('\\', '/')
            val text: String = file.readText()
            val count: Int = rawDp.findAll(text).count() + rawHex.findAll(text).count()
            val allowed: Int = rawStyleBaseline[rel] ?: 0
            if (count > allowed) {
                offenders += "$rel: $count raw hex/dp literal(s), baseline allows $allowed"
            }
        }
        if (offenders.isNotEmpty()) {
            fail(
                "New raw color/dp literals in feature screens — use LocalTokens / LocalSpacing, not Color(0x…) / N.dp.\n" +
                    "If you tokenized a file, LOWER its number in rawStyleBaseline (never raise). Offenders:\n" +
                    offenders.joinToString("\n")
            )
        }
    }

    @Test
    fun feature_screens_use_the_component_catalogue_not_raw_material3() {
        val root: File = featureRoot()
        val offenders: MutableList<String> = mutableListOf()
        root.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            val rel: String = file.relativeTo(root).path.replace('\\', '/')
            file.readLines().forEachIndexed { index, line ->
                if (offCatalogue.containsMatchIn(line)) {
                    offenders += "$rel:${index + 1}: ${line.trim()}"
                }
            }
        }
        if (offenders.isNotEmpty()) {
            fail(
                "Off-catalogue Material3 primitive imported in a feature screen — use the design-system component " +
                    "wrapper instead. Offenders:\n" + offenders.joinToString("\n")
            )
        }
    }

    private fun featureRoot(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate =
                File(dir, "app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/feature")
            if (candidate.isDirectory) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate feature source from ${System.getProperty("user.dir")}")
    }
}
