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
            "chat/ui/ChatScreen.kt" to 15,
            "chat/ui/ChatMessageFragments.kt" to 1, // emote-image size (24.dp) — moved here from ChatScreen's inline loop
            "connect/ui/ProviderBrand.kt" to 9, // provider brand colors (Twitch/Spotify/…) — likely permanent
            "connect/ui/ConnectModal.kt" to 8,
            "connect/ui/ConnectModalGlyphs.kt" to 6,
            "shell/ui/ShellGlyphs.kt" to 4,
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

    // Any Material interactive primitive bypasses NomNomzBot's visual and behavior contract. This broader
    // pattern also catches aliases, icon buttons, radio/segmented controls, and less common M3 variants.
    private val materialControlImport: Regex =
        Regex(
            "^import androidx\\.compose\\.material3\\." +
                "[A-Za-z]*(Button|Card|TextField|Checkbox|Switch|Slider|Chip)(?:\\s+as\\s+\\w+)?\\s*$"
        )
    private val materialWildcardImport: Regex = Regex("^import androidx\\.compose\\.material3\\.\\*\\s*$")
    private val fullyQualifiedMaterialControl: Regex =
        Regex(
            "androidx\\.compose\\.material3\\." +
                "[A-Za-z]*(Button|Card|TextField|Checkbox|Switch|Slider|Chip)\\s*\\("
        )
    private val allowedMaterialControlImports: Map<String, String> =
        mapOf(
            // Slider is explicitly the catalogue's M3-wrapped accessibility primitive; its wrapper
            // supplies every color and is the only approved native-control seam.
            "core/designsystem/component/Slider.kt" to
                "import androidx.compose.material3.Slider as Material3Slider"
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

    @Test
    fun common_ui_never_bypasses_the_component_catalogue() {
        val root: File = commonMainRoot()
        val offenders: MutableList<String> = mutableListOf()
        root.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            val rel: String = file.relativeTo(root).path.replace('\\', '/')
            file.readLines().forEachIndexed { index, line ->
                if (
                    (materialControlImport.containsMatchIn(line) ||
                        materialWildcardImport.containsMatchIn(line) ||
                        fullyQualifiedMaterialControl.containsMatchIn(line)) &&
                        allowedMaterialControlImports[rel] != line.trim()
                ) {
                    offenders += "$rel:${index + 1}: ${line.trim()}"
                }
            }
        }
        if (offenders.isNotEmpty()) {
            fail(
                "Raw Material controls bypass the NomNomzBot component catalogue:\n" +
                    offenders.joinToString("\n")
            )
        }
    }

    // ── Sleak core rules that a static check can actually decide ────────────────────────────────
    //
    // The guards above check TOKENS: the right color, the right spacing unit. They cannot see
    // HIERARCHY, which is what the Sleak skill governs and what shipped wrong — a screen can use
    // every correct token and still tell the reader nothing about what matters. These two catch the
    // parts of that which are decidable from source. Both stand at ZERO because the tree was brought
    // to zero when they were written: there is no baseline to burn down, so any offender is new.

    /**
     * Sleak core rule 1, concentric radius. A design-system [Card] is `radius.lg` with its own
     * padding; nesting a second one inside it puts two identical curves a padding-width apart, which
     * is the exact geometry the rule forbids (`parent − padding = child`). The catalogue has no
     * radius that satisfies that relationship at `s4`, so the fix is never "pick a smaller radius" —
     * it is not to nest. Group with spacing and Separators instead.
     */
    @Test
    fun cards_are_never_nested_inside_cards() {
        val root: File = featureRoot()
        val offenders: MutableList<String> = mutableListOf()
        root.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            val rel: String = file.relativeTo(root).path.replace('\\', '/')
            val text: String = withoutImports(file.readText())
            cardCall.findAll(text).forEach { match ->
                val body: IntRange = trailingLambdaOf(text, match.range.last) ?: return@forEach
                if (cardCall.containsMatchIn(text.substring(body))) {
                    offenders += "$rel:${text.lineOf(match.range.first)}"
                }
            }
        }
        if (offenders.isNotEmpty()) {
            fail(
                "Card nested directly inside a Card — two identical radii separated by padding breaks " +
                    "concentric radius (Sleak core rule 1). Group with spacing/Separator, or give the " +
                    "outer surface no Card. Offenders:\n" + offenders.joinToString("\n")
            )
        }
    }

    /**
     * Sleak core rule 3, hierarchy by weight. A destructive action must not look like its neighbours.
     * The catalogue ships `Destructive`, `DestructiveSecondary` and `DestructiveGhost` for exactly
     * this; a destructive-token label colour or a trash glyph carries the same signal, so all three
     * count. What fails is a ban / delete / revoke rendered identically to the save button beside it.
     *
     * Words like "dismiss" or "cancel" in the same call mean the button is the SAFE half of a
     * destructive dialog, which must stay quiet — those are exempt, not offenders.
     */
    @Test
    fun destructive_actions_are_visually_distinct() {
        val root: File = featureRoot()
        val offenders: MutableList<String> = mutableListOf()
        root.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            val rel: String = file.relativeTo(root).path.replace('\\', '/')
            val text: String = withoutImports(file.readText())
            buttonCall.findAll(text).forEach { match ->
                val body: IntRange = trailingLambdaOf(text, match.range.last) ?: match.range
                val call: String = text.substring(match.range.first, body.last + 1)
                if (
                    destructiveVerb.containsMatchIn(call) &&
                        !safeHalfOfDialog.containsMatchIn(call) &&
                        !destructiveSignal.containsMatchIn(call)
                ) {
                    offenders += "$rel:${text.lineOf(match.range.first)}"
                }
            }
        }
        if (offenders.isNotEmpty()) {
            fail(
                "Destructive action with no destructive treatment — use ButtonVariant.Destructive* , a " +
                    "tokens.destructive label, or TrashGlyph, so it does not read as an ordinary action " +
                    "(Sleak core rule 3). Offenders:\n" + offenders.joinToString("\n")
            )
        }
    }

    private val cardCall: Regex = Regex("""(?<![A-Za-z0-9_.])Card\s*\(""")
    private val buttonCall: Regex = Regex("""(?<![A-Za-z0-9_.])(Button|TextButton|GlyphButton)\s*\(""")
    private val destructiveVerb: Regex =
        Regex("""(?<![A-Za-z])(?i)(ban|delete|remove|revoke|purge|disconnect|unlink|wipe|destroy)(?![A-Za-z])""")
    private val safeHalfOfDialog: Regex =
        Regex("""(?<![A-Za-z])(?i)(dismiss|cancel|close|keep|back|restore|undo)(?![A-Za-z])""")
    private val destructiveSignal: Regex =
        Regex("""ButtonVariant\.Destructive|\.destructive|TrashGlyph""")

    /** Blanks import lines so matches never fire on them, while keeping line numbers honest. */
    private fun withoutImports(text: String): String =
        text.lineSequence().joinToString("\n") { if (it.trimStart().startsWith("import ")) "" else it }

    private fun String.lineOf(index: Int): Int = substring(0, index).count { it == '\n' } + 1

    /**
     * The span of a composable call's trailing `{ … }` block, or null when it has none. Balances
     * the argument list first, because a nested `(` inside the arguments would otherwise end it early.
     */
    private fun trailingLambdaOf(text: String, callParenIndex: Int): IntRange? {
        var depth = 0
        var i: Int = callParenIndex
        while (i < text.length) {
            if (text[i] == '(') depth++
            if (text[i] == ')') {
                depth--
                if (depth == 0) break
            }
            i++
        }
        var k: Int = i + 1
        while (k < text.length && text[k].isWhitespace()) k++
        if (k >= text.length || text[k] != '{') return null
        var braces = 0
        var end: Int = k
        while (end < text.length) {
            if (text[end] == '{') braces++
            if (text[end] == '}') {
                braces--
                if (braces == 0) break
            }
            end++
        }
        return k..minOf(end, text.length - 1)
    }

    private fun featureRoot(): File {
        return File(commonMainRoot(), "feature")
    }

    private fun commonMainRoot(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate =
                File(dir, "app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard")
            if (candidate.isDirectory) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate commonMain source from ${System.getProperty("user.dir")}")
    }
}
