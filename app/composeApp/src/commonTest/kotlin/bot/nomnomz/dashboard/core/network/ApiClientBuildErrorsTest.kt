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
import kotlin.test.assertTrue

// A rejected project save answers 400 with the StatusResponseDto envelope whose `data.errors` lists every build
// problem with its file, line and column. The editor can only underline those lines if the client keeps them.
class ApiClientBuildErrorsTest {
    private fun client(): ApiClient = ApiClient(baseUrlProvider = { null }, tokenProvider = { null })

    @Test
    fun a_failed_save_keeps_every_build_error_with_its_position() {
        val body: String =
            """{"status":"error","message":"The project has 2 errors.","code":"VALIDATION_FAILED","data":{"errors":[
                {"code":"build","message":"lib.ts:2:18: Unexpected \";\"","file":"lib.ts","line":2,"column":18},
                {"code":"build","message":"Entry file is missing."}]}}"""

        val error: ApiError = client().errorFromBody(400, "Bad Request", body)

        assertEquals("The project has 2 errors.", error.message)
        assertEquals("VALIDATION_FAILED", error.code)
        assertEquals(
            listOf(
                BuildError(code = "build", message = "lib.ts:2:18: Unexpected \";\"", file = "lib.ts", line = 2, column = 18),
                BuildError(code = "build", message = "Entry file is missing.", file = null, line = null, column = null),
            ),
            error.errors,
        )
    }

    @Test
    fun a_failure_without_data_has_no_errors_and_keeps_its_message() {
        val body: String = """{"status":"error","message":"Nope.","code":"NOT_FOUND"}"""

        val error: ApiError = client().errorFromBody(404, "Not Found", body)

        assertTrue(error.errors.isEmpty())
        assertEquals("Nope.", error.message)
    }

    @Test
    fun a_data_field_of_another_shape_never_costs_the_failure_its_message() {
        val body: String = """{"status":"error","message":"Over the limit.","code":"LIMIT_REACHED","data":"free tier"}"""

        val error: ApiError = client().errorFromBody(409, "Conflict", body)

        assertEquals("Over the limit.", error.message)
        assertEquals("LIMIT_REACHED", error.code)
        assertTrue(error.errors.isEmpty())
    }
}
