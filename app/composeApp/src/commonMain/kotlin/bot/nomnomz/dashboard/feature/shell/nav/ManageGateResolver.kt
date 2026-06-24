// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.nav

import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.manage_gate_requires
import nomnomzbot.composeapp.generated.resources.shell_role_broadcaster
import nomnomzbot.composeapp.generated.resources.shell_role_editor
import nomnomzbot.composeapp.generated.resources.shell_role_moderator
import nomnomzbot.composeapp.generated.resources.shell_role_supermod

// The bridge between the role/floor model (`ShellNav`) and the design-system write gate (`ManageDecision`).
// One place owns "is this caller above the floor, and if not, what's the localized reason?" — screens call
// `rememberManageDecision(...)` and hand the result to `ManageGate`, never re-deriving the rule. This keeps the
// gate primitive design-system-pure (no `ManagementRole`/`ShellRoute` leak) and the role→reason mapping single.

/**
 * Resolve the write-gate [ManageDecision] for the caller's [role] on a [route]'s [action] (default = the page's
 * own manage floor; a named [ManageAction] for the spec's sub-page exceptions). Returns [ManageDecision.Allowed]
 * when the role clears the floor (or the control is read-only and ungated), else [ManageDecision.Denied] with a
 * localized "Requires <role>" reason naming the minimum role. `@Composable` because the reason is a localized
 * string; pure otherwise — the same inputs always yield the same decision.
 */
@Composable
fun rememberManageDecision(
    role: ManagementRole?,
    route: ShellRoute,
    action: ManageAction = ManageAction.Default,
): ManageDecision {
    if (ShellNav.canManage(role, route, action)) return ManageDecision.Allowed

    // Denied: name the floor the caller must reach. A null floor would mean an ungated/read-only control, which
    // canManage already treats as not-manageable — there is no floor to name, so fall back to Allowed rather
    // than inventing a reason (a read-only control simply isn't a write affordance to gate).
    val floor: ManagementRole = ShellNav.manageFloorFor(route, action) ?: return ManageDecision.Allowed
    val reason: String = stringResource(Res.string.manage_gate_requires, stringResource(floor.labelResource()))
    return ManageDecision.Denied(reason = reason)
}

/**
 * Resolve a [ManageDecision] against an EXPLICIT [floor] rather than a route's page/action floor. The Settings
 * page is read-only at the nav level (no single manage floor) because its tabs each carry their own floor
 * (frontend-ia.md §5: Bot basics = Editor, Danger zone / Roles / Billing = Broadcaster). A Settings tab passes
 * its own floor here to gate its controls — Allowed when the caller's [role] clears it, else Denied with the
 * localized "Requires <role>" reason. Keeps the role→reason mapping in this one bridge.
 */
@Composable
fun rememberManageDecisionAtFloor(role: ManagementRole?, floor: ManagementRole): ManageDecision {
    if (role != null && role.level >= floor.level) return ManageDecision.Allowed
    val reason: String = stringResource(Res.string.manage_gate_requires, stringResource(floor.labelResource()))
    return ManageDecision.Denied(reason = reason)
}

/** The localized display label for a [ManagementRole], shared with the profile badge (single source). */
private fun ManagementRole.labelResource(): StringResource =
    when (this) {
        ManagementRole.Moderator -> Res.string.shell_role_moderator
        ManagementRole.SuperMod -> Res.string.shell_role_supermod
        ManagementRole.Editor -> Res.string.shell_role_editor
        ManagementRole.Broadcaster -> Res.string.shell_role_broadcaster
    }
