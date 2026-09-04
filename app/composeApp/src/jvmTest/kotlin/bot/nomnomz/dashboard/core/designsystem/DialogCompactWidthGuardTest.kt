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

// S-UX-6a: every dialog in the app (AlertDialog/ConfirmDialog/StepFormDialog/…) is built on the shared `Dialog`
// primitive (core/designsystem/component/Dialog.kt), which used to cap its width at a flat 512dp
// (`sm:max-w-lg`) with nothing bounding it to the actual viewport. Compose's window `Dialog` sets
// `usePlatformDefaultWidth = false` and has no size of its own, so on a Compact (phone-width) screen that
// 512dp card is wider than the device — the "dialog wider than a phone" defect. The fix is in the ONE
// primitive every dialog already goes through, not fifty call sites: at Compact it sizes to a fraction of the
// viewport instead of the fixed max.
//
// Two things have to hold for that fix to actually protect the app:
//   1. The primitive itself really branches on the size class (a revert back to a bare `widthIn(max = ...)`
//      would silence this without a screen ever noticing).
//   2. Nothing hand-rolls its own `androidx.compose.ui.window.Dialog` and bypasses the primitive, which
//      would reintroduce the flat-512dp bug in exactly one call site at a time.
class DialogCompactWidthGuardTest {

    private val dialogPrimitive: File =
        File("src/commonMain/kotlin/bot/nomnomz/dashboard/core/designsystem/component/Dialog.kt")

    @Test
    fun the_dialog_primitive_branches_its_width_on_the_size_class() {
        val source: String = dialogPrimitive.readText()

        if (!source.contains("windowSize.isCompact")) {
            fail(
                "Dialog.kt no longer branches on the size class — every AlertDialog/ConfirmDialog/form " +
                    "dialog in the app would silently go back to a flat max width that overflows a phone"
            )
        }
        if (!source.contains("fillMaxWidth(CompactDialogWidthFraction)")) {
            fail(
                "Dialog.kt's Compact branch no longer sizes to a fraction of the viewport — check it still " +
                    "narrows the card instead of leaving the fixed 512dp cap in place at every width"
            )
        }
    }

    @Test
    fun no_screen_hand_rolls_its_own_window_dialog_bypassing_the_compact_aware_primitive() {
        // Enumerated from the real source tree, not a hand-typed list of "known" offenders — a screen added
        // tomorrow is caught the same way the ones that exist today are.
        val offenders: MutableList<String> =
            File("src/commonMain/kotlin/bot/nomnomz/dashboard")
                .walkTopDown()
                .filter { it.isFile && it.extension == "kt" && it != dialogPrimitive }
                .filter { it.readText().contains("androidx.compose.ui.window.Dialog") }
                .map { it.path }
                .toMutableList()

        // core/designsystem/component/Sheet.kt is a separate, deliberately full-bleed primitive (a bottom/side
        // sheet, not a centred confirm/form dialog) — it is not a bypass of Dialog.kt and carries its own width
        // behavior. Nothing else is allowed to touch the raw window Dialog API.
        offenders.removeAll { it.replace('\\', '/').endsWith("core/designsystem/component/Sheet.kt") }

        if (offenders.isNotEmpty()) {
            fail(
                "these files call the raw window Dialog API directly instead of the DS Dialog/AlertDialog " +
                    "primitive, so they never get its Compact-width fix: $offenders"
            )
        }
    }
}
