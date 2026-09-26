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

// The act-as picker's people come from GET /admin/tenants/{id}/members; this pins the real client's verb, path
// and query (paged, search percent-encoded, omitted when blank) against a real ApiClient with no network.
class TenantMembersApiTest {

    private fun spied(): Pair<TenantMembersApi, MutableList<Pair<String, String>>> {
        val client = ApiClient(baseUrlProvider = { null }, tokenProvider = { null })
        val calls: MutableList<Pair<String, String>> = mutableListOf()
        client.requestSpy = { method, path -> calls += method to path }
        return RestTenantMembersApi(client) to calls
    }

    @Test
    fun lists_the_tenants_people_paged_with_the_search_encoded() = runTest {
        val (api, calls) = spied()

        api.listMembers(broadcasterId = "chan-1", search = "mod mia")

        assertEquals(listOf("GET" to "api/v1/admin/tenants/chan-1/members?page=1&take=25&search=mod%20mia"), calls)
    }

    @Test
    fun a_blank_search_sends_no_search_parameter() = runTest {
        val (api, calls) = spied()

        api.listMembers(broadcasterId = "chan-1", search = "  ", page = 2, pageSize = 10)

        assertEquals(listOf("GET" to "api/v1/admin/tenants/chan-1/members?page=2&take=10"), calls)
    }
}
