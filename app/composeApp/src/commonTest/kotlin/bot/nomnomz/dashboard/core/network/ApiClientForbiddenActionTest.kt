// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// A forbidden action answers 403 problem details carrying the action, the role it needs and the role the caller holds.
class ApiClientForbiddenActionTest {
    private fun client(): ApiClient = ApiClient(baseUrlProvider = { null }, tokenProvider = { null })

    @Test
    fun a_forbidden_action_keeps_the_action_and_both_role_names() {
        val body: String =
            """{"type":"https://nomnomz.bot/problems/forbidden-action","title":"Forbidden","status":403,
                "detail":"'timers:write' needs the Editor role in this channel; you hold Moderator.",
                "code":"FORBIDDEN_ACTION","action":"timers:write","requiredRole":"Editor","heldRole":"Moderator"}"""

        val error: ApiError = client().errorFromBody(403, "Forbidden", body)

        assertEquals(403, error.status)
        assertEquals("FORBIDDEN_ACTION", error.code)
        assertEquals("timers:write", error.action)
        assertEquals("Editor", error.requiredRole)
        assertEquals("Moderator", error.heldRole)
    }

    @Test
    fun an_empty_403_has_no_action_fields() {
        val error: ApiError = client().errorFromBody(403, "Forbidden", "")

        assertEquals("403", error.code)
        assertNull(error.action)
        assertNull(error.requiredRole)
        assertNull(error.heldRole)
    }
}
