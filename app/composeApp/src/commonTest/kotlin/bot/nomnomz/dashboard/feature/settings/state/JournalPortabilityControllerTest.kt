// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.state

import bot.nomnomz.dashboard.core.io.JournalFileIO
import bot.nomnomz.dashboard.core.io.PickedFile
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.EventJournalImportSummary
import bot.nomnomz.dashboard.core.network.EventStoreApi
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Event Journal export/import state machine: export pulls the channel's real bytes and hands them to
// the file bridge to save; import picks a file and uploads exactly its bytes, surfacing the backend's import
// counts. Each test asserts the consequence — the bytes that flowed, the channel targeted, the summary surfaced —
// not merely that a call happened.
class JournalPortabilityControllerTest {

    @Test
    fun export_fetches_the_channels_bytes_and_saves_them() = runTest {
        val payload: ByteArray = "{\"eventId\":\"a\"}\n{\"eventId\":\"b\"}\n".encodeToByteArray()
        val eventStore = FakeEventStoreApi(exportResult = ApiResult.Ok(payload))
        val bridge = FakeFileBridge(saveResult = true)
        val controller =
            JournalPortabilityController(
                FakeJournalChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventStore,
                bridge,
            )

        controller.export()

        // The export targeted the resolved channel and the EXACT bytes the backend returned were handed to save.
        assertEquals("ch1", eventStore.lastExportChannelId)
        assertContentEquals(payload, bridge.savedBytes)
        assertEquals("event-journal-ch1.jsonl", bridge.savedName)

        val state: JournalPortabilityState = controller.state.value
        assertTrue(state.exported, "a completed save flags the section as exported")
        assertEquals(false, state.busy)
        assertNull(state.error)
        assertNull(state.imported)
    }

    @Test
    fun export_surfaces_a_backend_failure_and_never_saves() = runTest {
        val bridge = FakeFileBridge(saveResult = true)
        val controller =
            JournalPortabilityController(
                FakeJournalChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeEventStoreApi(exportResult = ApiResult.Failure(ApiError(500, "ERR", "export boom"))),
                bridge,
            )

        controller.export()

        val state: JournalPortabilityState = controller.state.value
        assertEquals("export boom", state.error)
        assertEquals(false, state.exported)
        assertNull(bridge.savedBytes, "a failed export never reaches the file save")
    }

    @Test
    fun export_cancelled_save_returns_to_idle_not_exported() = runTest {
        val payload: ByteArray = "{\"eventId\":\"a\"}\n".encodeToByteArray()
        val controller =
            JournalPortabilityController(
                FakeJournalChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeEventStoreApi(exportResult = ApiResult.Ok(payload)),
                FakeFileBridge(saveResult = false),
            )

        controller.export()

        val state: JournalPortabilityState = controller.state.value
        assertEquals(false, state.exported, "a cancelled save is not a completed export")
        assertEquals(false, state.busy)
        assertNull(state.error)
    }

    @Test
    fun import_uploads_the_picked_file_and_surfaces_the_summary() = runTest {
        val picked: ByteArray = "{\"eventId\":\"a\"}\n{\"eventId\":\"b\"}\n".encodeToByteArray()
        val summary = EventJournalImportSummary(totalLines = 2, imported = 2, skippedDuplicate = 0, upcast = 1)
        val eventStore = FakeEventStoreApi(importResult = ApiResult.Ok(summary))
        val controller =
            JournalPortabilityController(
                FakeJournalChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventStore,
                FakeFileBridge(pickResult = PickedFile(name = "backup.jsonl", bytes = picked)),
            )

        controller.import()

        // Exactly the picked file's name + bytes were uploaded to the resolved channel.
        assertEquals("ch1", eventStore.lastImportChannelId)
        assertEquals("backup.jsonl", eventStore.lastImportFileName)
        assertContentEquals(picked, eventStore.lastImportBytes)

        // The backend's import counts are surfaced verbatim.
        val state: JournalPortabilityState = controller.state.value
        assertEquals(summary, state.imported)
        assertEquals(false, state.busy)
        assertNull(state.error)
    }

    @Test
    fun import_cancelled_pick_makes_no_upload() = runTest {
        val eventStore = FakeEventStoreApi(importResult = ApiResult.Ok(EventJournalImportSummary()))
        val controller =
            JournalPortabilityController(
                FakeJournalChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventStore,
                FakeFileBridge(pickResult = null),
            )

        controller.import()

        assertNull(eventStore.lastImportBytes, "a cancelled file pick never uploads")
        val state: JournalPortabilityState = controller.state.value
        assertNull(state.imported)
        assertEquals(false, state.busy)
        assertNull(state.error)
    }

    @Test
    fun import_empty_file_is_rejected_before_upload() = runTest {
        val eventStore = FakeEventStoreApi(importResult = ApiResult.Ok(EventJournalImportSummary()))
        val controller =
            JournalPortabilityController(
                FakeJournalChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                eventStore,
                FakeFileBridge(pickResult = PickedFile(name = "empty.jsonl", bytes = ByteArray(0))),
            )

        controller.import()

        assertNull(eventStore.lastImportBytes, "an empty file is rejected before the upload")
        assertTrue(controller.state.value.error != null, "the empty file surfaces an error")
    }

    @Test
    fun import_surfaces_a_backend_failure() = runTest {
        val controller =
            JournalPortabilityController(
                FakeJournalChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeEventStoreApi(importResult = ApiResult.Failure(ApiError(409, "ERR", "import boom"))),
                FakeFileBridge(
                    pickResult = PickedFile(name = "x.jsonl", bytes = "{}".encodeToByteArray()),
                ),
            )

        controller.import()

        assertEquals("import boom", controller.state.value.error)
        assertNull(controller.state.value.imported)
    }
}

private class FakeJournalChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeEventStoreApi(
    private val exportResult: ApiResult<ByteArray> = ApiResult.Ok(ByteArray(0)),
    private val importResult: ApiResult<EventJournalImportSummary> =
        ApiResult.Ok(EventJournalImportSummary()),
) : EventStoreApi {
    var lastExportChannelId: String? = null
        private set

    var lastImportChannelId: String? = null
        private set

    var lastImportFileName: String? = null
        private set

    var lastImportBytes: ByteArray? = null
        private set

    override suspend fun exportJournal(channelId: String): ApiResult<ByteArray> {
        lastExportChannelId = channelId
        return exportResult
    }

    override suspend fun importJournal(
        channelId: String,
        fileName: String,
        bytes: ByteArray,
    ): ApiResult<EventJournalImportSummary> {
        lastImportChannelId = channelId
        lastImportFileName = fileName
        lastImportBytes = bytes
        return importResult
    }
}

private class FakeFileBridge(
    private val saveResult: Boolean = true,
    private val pickResult: PickedFile? = null,
) : JournalFileIO {
    var savedName: String? = null
        private set

    var savedBytes: ByteArray? = null
        private set

    override suspend fun saveFile(suggestedName: String, bytes: ByteArray): Boolean {
        savedName = suggestedName
        savedBytes = bytes
        return saveResult
    }

    override suspend fun pickFile(): PickedFile? = pickResult
}
