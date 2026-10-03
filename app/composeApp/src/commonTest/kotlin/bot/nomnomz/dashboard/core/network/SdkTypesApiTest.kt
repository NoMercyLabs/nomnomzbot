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

// The script editor types `bot.getVar` with the keys of the triggers that run the open script, so the request
// for a saved script must carry `script=<id>`; a request with no script id keeps the plain context-only URL.
class SdkTypesApiTest {

    private fun spied(): Pair<SdkTypesApi, MutableList<Pair<String, String>>> {
        val client = ApiClient(baseUrlProvider = { null }, tokenProvider = { null })
        val calls: MutableList<Pair<String, String>> = mutableListOf()
        client.requestSpy = { method, path -> calls += method to path }
        return RestSdkTypesApi(client) to calls
    }

    @Test
    fun a_saved_script_adds_its_id_to_the_request() = runTest {
        val (api, calls) = spied()

        api.types("script", scriptId = "0b6f1c1e-1111-4222-8333-444455556666")

        assertEquals(
            listOf("GET" to "api/v1/sdk/types.d.ts?context=script&script=0b6f1c1e-1111-4222-8333-444455556666"),
            calls,
        )
    }

    @Test
    fun a_new_script_without_an_id_sends_no_script_parameter() = runTest {
        val (api, calls) = spied()

        api.types("script", scriptId = null)
        api.types("widget")

        assertEquals(
            listOf(
                "GET" to "api/v1/sdk/types.d.ts?context=script",
                "GET" to "api/v1/sdk/types.d.ts?context=widget",
            ),
            calls,
        )
    }
}
