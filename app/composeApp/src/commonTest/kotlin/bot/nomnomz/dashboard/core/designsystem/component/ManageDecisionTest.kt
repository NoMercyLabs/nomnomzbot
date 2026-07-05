// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

// Proves the value the write gate renders. `ManageGate` is a thin renderer over a settled `ManageDecision`: an
// Allowed decision must enable the control and announce nothing; a Denied decision must disable it and carry the
// exact reason a screen reader speaks. These two invariants ARE the gate's contract — if they break, a denied
// control would either act anyway (Allowed leaking through) or fall silent (no reason announced), so the test
// fails for the right reason.
class ManageDecisionTest {

    @Test
    fun an_allowed_decision_enables_the_control_and_announces_no_reason() {
        val decision: ManageDecision = ManageDecision.Allowed

        assertTrue(decision.isAllowed, "an Allowed decision must enable the gated control")
        assertNull(decision.deniedReason, "an Allowed decision has nothing to announce")
    }

    @Test
    fun a_denied_decision_disables_the_control_and_carries_the_exact_reason() {
        // The reason arrives already localized from the shell bridge ("Requires Editor" / "Vereist Editor").
        val decision: ManageDecision = ManageDecision.Denied(reason = "Requires Editor")

        assertFalse(decision.isAllowed, "a Denied decision must disable the gated control")
        assertEquals(
            "Requires Editor",
            decision.deniedReason,
            "a Denied decision must surface the verbatim reason for assistive tech to announce",
        )
    }

    @Test
    fun a_denied_decision_tolerates_a_transient_blank_reason_instead_of_freezing_the_page() {
        // On Wasm, Compose string resources resolve asynchronously — the localized reason reads back as "" for the
        // first frame(s) of a screen before the bundle loads, then recomposes to the real text. This gate sits on
        // every screen's render hot path, so a hard "reason must be non-blank" invariant would turn that millisecond
        // of un-loaded i18n into a full page freeze (exactly the crash we are eliminating). Constructing a Denied
        // with a blank reason must therefore NOT throw: it still disables the control, and the gate announces the
        // reason once it recomposes in. If this regressed to throwing, the whole page would white-screen mid-load.
        val blank: ManageDecision = ManageDecision.Denied(reason = "")
        val whitespace: ManageDecision = ManageDecision.Denied(reason = "   ")

        assertFalse(blank.isAllowed, "a Denied decision still disables its control while i18n is mid-load")
        assertFalse(whitespace.isAllowed, "a Denied decision still disables its control while i18n is mid-load")
    }
}
