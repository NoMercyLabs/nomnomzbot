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

import io.ktor.http.ContentType
import kotlinx.serialization.Serializable

// The typed facade for portable event-journal export/import (backend EventStoreController, management plane,
// Broadcaster-floored). Export downloads the channel's whole journal as a JSONL file; import uploads such a file
// back, idempotently (duplicates by EventId are skipped) and atomically.
//
// Backend routes:
//   POST /api/v1/event-store/channels/{channelId}/export               → JSONL file (application/x-ndjson)
//   POST /api/v1/event-store/channels/{channelId}/import               ← multipart file → StatusResponseDto<EventJournalImportSummary>
//   POST /api/v1/event-store/channels/{channelId}/rebuild-projections  → StatusResponseDto<string> (task ID)
interface EventStoreApi {
    /** Exports the channel's whole event journal as raw JSONL bytes (one event envelope per line). */
    suspend fun exportJournal(channelId: String): ApiResult<ByteArray>

    /** Imports a JSONL export into the channel's journal; returns the import/skip/upcast counts. */
    suspend fun importJournal(
        channelId: String,
        fileName: String,
        bytes: ByteArray,
    ): ApiResult<EventJournalImportSummary>

    /** Enqueues a full projection rebuild for the channel. The backend returns a task-tracking ID. */
    suspend fun rebuildProjections(channelId: String): ApiResult<String>
}

class RestEventStoreApi(private val client: ApiClient) : EventStoreApi {
    override suspend fun exportJournal(channelId: String): ApiResult<ByteArray> =
        client.postBytes("api/v1/event-store/channels/$channelId/export")

    override suspend fun importJournal(
        channelId: String,
        fileName: String,
        bytes: ByteArray,
    ): ApiResult<EventJournalImportSummary> =
        client.postMultipartFile(
            path = "api/v1/event-store/channels/$channelId/import",
            fieldName = "file",
            fileName = fileName,
            bytes = bytes,
            contentType = ContentType("application", "x-ndjson"),
        )

    override suspend fun rebuildProjections(channelId: String): ApiResult<String> =
        client.postEnvelope("api/v1/event-store/channels/$channelId/rebuild-projections", Unit)
}

/**
 * The import outcome (backend `EventJournalImportSummary`): how many JSONL lines the file held, how many events
 * were newly appended, how many were skipped as already-present duplicates (idempotency), and how many were upcast
 * from a stale shape on the way in.
 */
@Serializable
data class EventJournalImportSummary(
    val totalLines: Long = 0,
    val imported: Long = 0,
    val skippedDuplicate: Long = 0,
    val upcast: Long = 0,
)
