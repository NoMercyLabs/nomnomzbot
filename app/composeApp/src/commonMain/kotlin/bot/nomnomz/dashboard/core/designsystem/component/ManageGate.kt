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

import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.disabled
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription

// The ONE write-gate primitive for the whole dashboard (frontend-ia.md §7: "disable-with-reason for actions
// below the manage floor"). Every page wraps each of its write controls — create / edit / delete / toggle /
// apply-action — in this gate instead of hand-rolling `enabled = ...` per screen, so the rule is identical
// everywhere: a caller below the action's manage floor sees the control rendered but disabled, with the reason
// announced to assistive tech.
//
// The gate is design-system-pure: it knows nothing of `ManagementRole` / `ShellRoute` (those live in the
// shell-nav layer). The caller resolves the role-vs-floor question once (via `ManageGate.decide`, the shell
// bridge) and hands this composable a settled [ManageDecision]; the gate only renders the consequence. This
// keeps the role→floor→localized-reason mapping in a single place and the primitive reusable.

/**
 * The settled outcome of a write-gate check: the caller either may act ([Allowed]) or is below the floor
 * ([Denied], carrying the already-localized [Denied.reason] — e.g. "Requires Editor"). A value, not a
 * computation — pure and trivially testable; the composable below only renders it.
 */
sealed interface ManageDecision {
    /** The caller clears the action's manage floor — the control is enabled. */
    data object Allowed : ManageDecision

    /** The caller is below the floor — the control is disabled and [reason] explains why (already localized). */
    data class Denied(val reason: String) : ManageDecision {
        init {
            require(reason.isNotBlank()) { "a denied manage decision must carry a non-blank reason" }
        }
    }

    /** True when the gated control may act. The single place the rest of the app reads "can I write?". */
    val isAllowed: Boolean
        get() = this is Allowed

    /** The reason to announce when denied; `null` when allowed (nothing to explain). */
    val deniedReason: String?
        get() = (this as? Denied)?.reason
}

/**
 * Render one write control through the gate. [content] receives `enabled = decision.isAllowed` and MUST pass it
 * straight to its control (`Button(enabled = enabled)`, `Switch(enabled = enabled)`, a `TextButton` whose
 * `onClick` is only wired when enabled). When [decision] is [ManageDecision.Denied] the gate wraps the control
 * in a node that is marked `disabled` and carries the reason as its `stateDescription`, so a screen reader
 * announces *why* the control is inert (a11y: the reason is announced, not silent). When allowed the gate adds
 * no semantics — the control speaks for itself.
 *
 * One primitive, every screen: a Mod on Commands wraps "New command" in `ManageGate(decision) { Button(enabled =
 * it, ...) }` and the button renders disabled with "Requires Editor"; a Broadcaster's identical call renders it
 * live. The screen never writes `enabled =` by hand.
 */
@Composable
fun ManageGate(
    decision: ManageDecision,
    modifier: Modifier = Modifier,
    content: @Composable (enabled: Boolean) -> Unit,
) {
    val reason: String? = decision.deniedReason
    val gateModifier: Modifier =
        if (reason != null) {
            modifier.semantics {
                disabled()
                stateDescription = reason
            }
        } else {
            modifier
        }

    Box(modifier = gateModifier) {
        content(decision.isAllowed)
    }
}
