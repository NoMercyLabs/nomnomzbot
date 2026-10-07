// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.customevents.state

import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
import bot.nomnomz.dashboard.core.network.CustomDataSource
import bot.nomnomz.dashboard.core.network.CustomDataSourceOption
import bot.nomnomz.dashboard.core.network.CustomDataSourcePreset
import bot.nomnomz.dashboard.core.network.CustomDataSourceTestFetch
import bot.nomnomz.dashboard.core.network.CustomEventsApi
import bot.nomnomz.dashboard.core.network.UpsertCustomDataSourceBody
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// The delete confirm stays open until the delete answers (psychology spec X1), so the controller hands the result
// back untouched: success reloads the list, a failure is returned for the dialog's inline error and fires no toast.
class CustomEventsControllerDeleteTest {
    @Test
    fun a_successful_delete_returns_ok_and_reloads_the_list_without_the_row() = runTest {
        val api: FakeCustomEventsApi = FakeCustomEventsApi(deleteResult = ApiResult.Ok(Unit))
        val controller: CustomEventsController = CustomEventsController(api, feedback = RecordingFeedback())

        val result: ApiResult<Unit> = controller.delete("s1")

        assertTrue(result is ApiResult.Ok)
        assertEquals(listOf("s1"), api.deleted)
        assertTrue(api.listCalls >= 1, "the list is reloaded after a successful delete")
    }

    @Test
    fun a_failed_delete_returns_the_failure_untouched_and_shows_no_toast() = runTest {
        val feedback: RecordingFeedback = RecordingFeedback()
        val api: FakeCustomEventsApi =
            FakeCustomEventsApi(deleteResult = ApiResult.Failure(ApiError(409, "IN_USE", "Still used by a pipeline.")))
        val controller: CustomEventsController = CustomEventsController(api, feedback = feedback)

        val result: ApiResult<Unit> = controller.delete("s1")

        assertEquals("Still used by a pipeline.", (result as ApiResult.Failure).error.message)
        assertEquals(0, api.listCalls, "a failed delete does not reload")
        assertTrue(feedback.messages.isEmpty())
    }
}

private class FakeCustomEventsApi(private val deleteResult: ApiResult<Unit>) : CustomEventsApi {
    val deleted: MutableList<String> = mutableListOf()
    var listCalls: Int = 0

    override suspend fun list(): ApiResult<List<CustomDataSource>> {
        listCalls++
        return ApiResult.Ok(emptyList())
    }

    override suspend fun listPresets(): ApiResult<List<CustomDataSourcePreset>> = ApiResult.Ok(emptyList())

    override suspend fun search(query: String, limit: Int): ApiResult<List<CustomDataSourceOption>> =
        ApiResult.Ok(emptyList())

    override suspend fun create(body: UpsertCustomDataSourceBody): ApiResult<CustomDataSource> =
        ApiResult.Ok(CustomDataSource())

    override suspend fun update(id: String, body: UpsertCustomDataSourceBody): ApiResult<CustomDataSource> =
        ApiResult.Ok(CustomDataSource())

    override suspend fun delete(id: String): ApiResult<Unit> {
        deleted += id
        return deleteResult
    }

    override suspend fun blastRadius(id: String): ApiResult<BlastRadiusSummary> = ApiResult.Ok(BlastRadiusSummary())

    override suspend fun test(id: String, samplePayload: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun testFetch(id: String): ApiResult<CustomDataSourceTestFetch> =
        ApiResult.Failure(ApiError(500, "NA", "not used"))
}
