// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import kotlin.test.Test
import kotlin.test.assertEquals

class PromoteCandidatesTest {

    @Test
    fun users_already_backing_a_principal_are_left_out() {
        val stoney = AdminUser(id = "u-1", displayName = "Stoney_Eagle", login = "stoney_eagle", role = "admin", channelCount = 1, createdAt = "2026-01-01T00:00:00Z")
        val mod = AdminUser(id = "u-2", displayName = "ModMax", login = "modmax", role = "user", channelCount = 0, createdAt = "2026-01-02T00:00:00Z")
        val principals = listOf(
            IamPrincipalSummary(id = "p-1", userId = "u-1", name = "Stoney"),
            IamPrincipalSummary(id = "p-2", principalType = 1, userId = null, name = "ci-bot"),
        )

        val candidates: List<AdminUser> = promoteCandidates(listOf(stoney, mod), principals)

        assertEquals(listOf(mod), candidates)
    }
}
