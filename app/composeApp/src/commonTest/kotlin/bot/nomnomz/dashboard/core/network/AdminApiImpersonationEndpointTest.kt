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

import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals

// A prior brief for S089b got this endpoint pairing wrong (POST admin/impersonate/{id}/end, which does not
// exist server-side) and it slipped past every other test because AdminController's tests fake the whole
// AdminApi interface — they prove the CONTROLLER calls api.endImpersonation with the right id, never that
// AdminApiImpl.endImpersonation sends the right HTTP verb+path. A wrong verb/path 404s in production while
// every fake-based test stays green, leaving the operator stuck in an act-as session with no way out. This
// test exercises the REAL AdminApiImpl against a real ApiClient (no network — [ApiClient.requestSpy] records
// the exact method+path before the base-URL check ever runs) so this specific regression cannot recur.
class AdminApiImpersonationEndpointTest {

    private fun apiClientWithSpy(): Pair<ApiClient, MutableList<Pair<String, String>>> {
        val client = ApiClient(baseUrlProvider = { null }, tokenProvider = { null })
        val calls: MutableList<Pair<String, String>> = mutableListOf()
        client.requestSpy = { method, path -> calls += method to path }
        return client to calls
    }

    @Test
    fun impersonate_POSTs_to_the_users_impersonate_route() = runTest {
        val (client, calls) = apiClientWithSpy()
        val api: AdminApi = AdminApiImpl(client)

        api.impersonate(subjectUserId = "user-1", accessGrantId = "grant-1", justification = "Investigating")

        assertEquals(listOf("POST" to "api/v1/admin/users/user-1/impersonate"), calls)
    }

    @Test
    fun end_impersonation_DELETEs_the_impersonation_session_route_not_a_post_to_slash_end() = runTest {
        val (client, calls) = apiClientWithSpy()
        val api: AdminApi = AdminApiImpl(client)

        api.endImpersonation(accessGrantId = "grant-1")

        // The exact regression this guards: this must be DELETE .../impersonation/{id}, never
        // POST .../impersonate/{id}/end (a route that does not exist on the backend).
        assertEquals(listOf("DELETE" to "api/v1/admin/impersonation/grant-1"), calls)
    }
}
