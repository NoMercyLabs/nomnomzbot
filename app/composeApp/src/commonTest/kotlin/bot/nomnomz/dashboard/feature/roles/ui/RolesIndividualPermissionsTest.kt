// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.roles.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.PermitGrant
import bot.nomnomz.dashboard.feature.roles.state.RolesState
import kotlin.test.Test
import kotlin.test.assertTrue

// People > Roles shows the per-user permit grants under the section name "Individual permissions" (the
// placement ledger's name for them), not under the old "Permits" label.
@OptIn(ExperimentalTestApi::class)
class RolesIndividualPermissionsTest {

    @Test
    fun the_per_user_grants_section_is_named_individual_permissions() = runComposeUiTest {
        val ready: RolesState.Ready =
            RolesState.Ready(
                members = emptyList(),
                permits = listOf(PermitGrant(id = "p1", userId = "u1", username = "viewer_one", capabilityActionKey = "chat:read")),
                grantableActions = emptyList(),
            )
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    RolesContent(
                        state = ready,
                        manage = ManageDecision.Allowed,
                        searchViewers = { emptyList() },
                        onAssignRole = { _, _ -> },
                        onRemoveRole = { ApiResult.Ok(Unit) },
                        onGrantCapability = { _, _, _, _ -> },
                        onGrantRole = { _, _, _, _ -> },
                        onRevoke = { _, _ -> ApiResult.Ok(Unit) },
                        onSetOverride = { _, _ -> },
                        onResetOverride = { },
                    )
                }
            }
        }

        assertTrue(
            onAllNodesWithText("Individual permissions").fetchSemanticsNodes().isNotEmpty(),
            "the grants section must be headed Individual permissions",
        )
        assertTrue(
            onAllNodesWithText("Permits").fetchSemanticsNodes().isEmpty(),
            "the old Permits heading must be gone",
        )
    }
}
