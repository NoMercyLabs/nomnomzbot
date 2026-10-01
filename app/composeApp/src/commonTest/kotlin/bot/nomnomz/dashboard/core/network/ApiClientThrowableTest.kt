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

import kotlinx.coroutines.Job
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

// The web dashboard froze on its spinner after a deploy (2026-10-01): a browser fetch that died mid-body threw
// a Kotlin/Wasm `JsException`, which is a Throwable but not an Exception. The client's `catch (Exception)` let it
// through, it crashed the calling Compose effect, and the recomposer died with it. A plain Throwable has the same
// shape on every platform, so these tests pin the behavior without a browser.
class ApiClientThrowableTest {

    private val failedFetch: Throwable = Throwable("Failed to fetch")

    private fun client(): ApiClient = ApiClient(baseUrlProvider = { null }, tokenProvider = { null })

    @Test
    fun a_body_read_that_throws_a_non_exception_becomes_a_failure_the_caller_can_render() = runTest {
        val result: ApiResult<String> = client().bodyFailure(status = 200, cause = failedFetch)

        val failure: ApiResult.Failure = assertIs<ApiResult.Failure>(result)
        assertEquals(200, failure.error.status)
        assertEquals("DESERIALIZATION", failure.error.code)
        assertEquals("Failed to fetch", failure.error.message)
    }

    @Test
    fun a_body_read_in_a_cancelled_scope_keeps_cancelling_instead_of_storing_a_failure() = runTest {
        val api: ApiClient = client()
        var stored: ApiResult<String>? = null

        val job: Job = launch {
            cancel()
            stored = api.bodyFailure(status = 200, cause = failedFetch)
        }
        job.join()

        assertTrue(job.isCancelled)
        assertNull(stored)
    }

    @Test
    fun a_transport_failure_in_a_cancelled_scope_keeps_cancelling_instead_of_storing_a_failure() = runTest {
        val api: ApiClient = client()
        var stored: ApiResult<String>? = null

        val job: Job = launch {
            cancel()
            stored = api.networkFailure(failedFetch)
        }
        job.join()

        assertTrue(job.isCancelled)
        assertNull(stored)
    }

    @Test
    fun a_refresher_that_throws_a_non_exception_counts_as_not_refreshed() = runTest {
        val api: ApiClient = client()
        api.tokenRefresher = { throw failedFetch }

        assertFalse(api.refreshAfterUnauthorized())
    }

    @Test
    fun a_refresher_that_succeeds_counts_as_refreshed() = runTest {
        val api: ApiClient = client()
        var calls = 0
        api.tokenRefresher = {
            calls++
            true
        }

        assertTrue(api.refreshAfterUnauthorized())
        assertEquals(1, calls)
    }
}
