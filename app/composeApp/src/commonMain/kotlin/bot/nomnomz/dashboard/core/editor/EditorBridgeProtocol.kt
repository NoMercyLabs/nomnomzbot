// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

import bot.nomnomz.dashboard.core.network.BuildError
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonObjectBuilder
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

// The wire protocol between Kotlin and the served editor page (`/editor/index.html`, whose editor.js owns
// Monaco, the file tree, preview and fire bar). Both hosts speak it: the web build over an iframe postMessage,
// the desktop build over the native web view's script bridge. One codec, so the two can never drift.
object EditorBridgeProtocol {
    const val READY: String = "nnz:editor:ready"
    const val OPEN: String = "nnz:editor:open"
    const val SAVE: String = "nnz:editor:save"
    const val COMPILED: String = "nnz:editor:compiled"
    const val CLOSE: String = "nnz:editor:close"
    /** Host to page: the host's own close control was used; the page answers like its Close button. */
    const val REQUEST_CLOSE: String = "nnz:editor:requestClose"
    const val HISTORY_LOAD_MORE: String = "nnz:editor:historyLoadMore"
    const val HISTORY_ROLLBACK: String = "nnz:editor:historyRollback"
    const val HISTORY_DELETE: String = "nnz:editor:historyDelete"
    const val HISTORY_PAGE: String = "nnz:editor:historyPage"
    const val HISTORY_ERROR: String = "nnz:editor:historyError"
    const val TEST_RUN: String = "nnz:editor:testRun"
    const val TEST_RUN_RESULT: String = "nnz:editor:testRunResult"

    /** Message types the PAGE sends to the host. Everything else on the channel is host-to-page traffic. */
    val pageToHostTypes: Set<String> =
        setOf(READY, SAVE, CLOSE, HISTORY_LOAD_MORE, HISTORY_ROLLBACK, HISTORY_DELETE, TEST_RUN)

    private val json: Json = Json { ignoreUnknownKeys = true }

    /**
     * Decodes one page-to-host message, or null when [raw] is not one: malformed JSON, a host-to-page type
     * echoed back, or unrelated traffic. Fields not meaningful for the type are dropped, so a `files` map on
     * anything but a save can never reach the compile path.
     */
    fun decode(raw: String): EditorInboundMessage? {
        val message: EditorInboundMessage =
            runCatching { json.decodeFromString(EditorInboundMessage.serializer(), raw) }.getOrNull() ?: return null
        if (message.type !in pageToHostTypes) return null
        return EditorInboundMessage(
            type = message.type,
            files = if (message.type == SAVE || message.type == TEST_RUN) message.files else emptyMap(),
            versionId =
                if (message.type == HISTORY_ROLLBACK || message.type == HISTORY_DELETE) message.versionId else "",
            variables = if (message.type == TEST_RUN) message.variables else emptyMap(),
            args = if (message.type == TEST_RUN) message.args else emptyList(),
            trigger = if (message.type == TEST_RUN) message.trigger else null,
            role = if (message.type == TEST_RUN) message.role else null,
        )
    }

    fun open(
        title: String,
        files: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String,
        sdkTypesUnavailable: Boolean,
        previewWidget: EditorPreviewWidget?,
        history: EditorHistory?,
        testRun: EditorTestRun?,
        uiLabels: Map<String, String> = emptyMap(),
    ): String =
        encode(
            buildJsonObject {
                put("type", OPEN)
                put(
                    "payload",
                    buildJsonObject {
                        put("title", title)
                        put("files", JsonObject(files.mapValues { entry -> JsonPrimitive(entry.value) }))
                        put("entry", entryPath)
                        put("language", language)
                        put("sdkTypes", sdkTypes)
                        put("sdkTypesUnavailable", sdkTypesUnavailable)
                        // The server's sample table, the one the widget Test button fires from.
                        put("fireSamples", previewWidget?.fireSamples ?: JsonObject(emptyMap()))
                        // The widget's PERSISTED subscription list — the fire bar's authoritative source.
                        put(
                            "eventSubscriptions",
                            JsonArray(previewWidget?.eventSubscriptions.orEmpty().map { event -> JsonPrimitive(event) }),
                        )
                        // What the preview injects as WIDGET_ID / WIDGET_NAME / WIDGET_SETTINGS, like the live page.
                        if (previewWidget != null) {
                            put(
                                "widget",
                                buildJsonObject {
                                    put("id", previewWidget.id)
                                    put("name", previewWidget.name)
                                    put("settings", previewWidget.settings)
                                },
                            )
                        }
                        // History / test-run panels are opt-in per caller; absent means the page hides them.
                        if (history != null) {
                            put("history", historyPageJson(history.initialVersions, history.initialHasMore))
                        }
                        put("testRunEnabled", testRun != null)
                        if (testRun != null) {
                            put("testTriggers", JsonArray(testRun.triggers.map { trigger -> triggerJson(trigger) }))
                        }
                        // One map for every word: the page's own labels, plus the test-run ones when a run is wired.
                        put("labels", labelsJson(uiLabels, testRun?.labels))
                    },
                )
            }
        )

    private fun triggerJson(trigger: EditorTestTrigger): JsonObject =
        buildJsonObject {
            put("id", trigger.id)
            put("label", trigger.label)
            put("variables", JsonObject(trigger.variables.mapValues { entry -> JsonPrimitive(entry.value) }))
        }

    private fun labelsJson(uiLabels: Map<String, String>, testRunLabels: EditorTestRunLabels?): JsonObject =
        buildJsonObject {
            for ((id: String, text: String) in uiLabels) put(id, text)
            if (testRunLabels != null) {
                put("manual", testRunLabels.manual)
                put("trigger", testRunLabels.trigger)
                put("role", testRunLabels.role)
                put("roles", JsonObject(testRunLabels.roles.mapValues { entry -> JsonPrimitive(entry.value) }))
                put("timeline", testRunLabels.timeline)
                put("timelineChat", testRunLabels.timelineChat)
                put("timelineEffect", testRunLabels.timelineEffect)
                put("timelineConsole", testRunLabels.timelineConsole)
            }
        }

    fun requestClose(): String = encode(buildJsonObject { put("type", REQUEST_CLOSE) })

    fun compiled(feedback: CompileFeedback): String =
        encode(
            buildJsonObject {
                put("type", COMPILED)
                put("ok", feedback.ok)
                put("message", feedback.message)
                putBuildErrors(feedback.errors)
            }
        )

    fun historyPage(page: EditorVersionsPage): String =
        encode(
            buildJsonObject {
                put("type", HISTORY_PAGE)
                put("payload", historyPageJson(page.versions, page.hasMore))
            }
        )

    fun historyError(message: String): String =
        encode(
            buildJsonObject {
                put("type", HISTORY_ERROR)
                put("message", message)
            }
        )

    fun testRunResult(result: EditorTestRunResult): String =
        encode(
            buildJsonObject {
                put("type", TEST_RUN_RESULT)
                put("ok", true)
                put("success", result.success)
                put("durationMs", result.durationMs)
                put("hostCallCount", result.hostCallCount)
                put("error", result.error)
                put("chatOutput", JsonArray(result.chatOutput.map { line -> JsonPrimitive(line) }))
                put(
                    "variablesSet",
                    result.variablesSet?.let { set -> JsonObject(set.mapValues { (_, value) -> JsonPrimitive(value) }) }
                        ?: JsonNull,
                )
                put(
                    "timeline",
                    JsonArray(
                        result.timeline.map { entry ->
                            buildJsonObject {
                                put("seq", entry.seq)
                                put("kind", entry.kind)
                                put("text", entry.text)
                            }
                        }
                    ),
                )
                put("console", result.console?.let { lines -> JsonArray(lines.map { line -> JsonPrimitive(line) }) } ?: JsonNull)
                put(
                    "effects",
                    JsonArray(
                        result.effects.map { effect ->
                            buildJsonObject {
                                put("name", effect.name)
                                put("argsPreview", effect.argsPreview)
                            }
                        }
                    ),
                )
                putBuildErrors(result.errors)
            }
        )

    // The located problems the editor underlines; absent (not empty) when there are none, so an older editor page
    // and a clean reply stay byte-identical to what they were before positions existed.
    private fun JsonObjectBuilder.putBuildErrors(errors: List<BuildError>) {
        if (errors.isEmpty()) return
        put(
            "errors",
            JsonArray(
                errors.map { error ->
                    buildJsonObject {
                        error.code?.let { code -> put("code", code) }
                        put("message", error.message)
                        error.file?.let { file -> put("file", file) }
                        error.line?.let { line -> put("line", line) }
                        error.column?.let { column -> put("column", column) }
                    }
                }
            ),
        )
    }

    fun testRunFailure(message: String): String =
        encode(
            buildJsonObject {
                put("type", TEST_RUN_RESULT)
                put("ok", false)
                put("message", message)
            }
        )

    private fun historyPageJson(versions: List<EditorVersionSummary>, hasMore: Boolean): JsonObject =
        buildJsonObject {
            put(
                "versions",
                JsonArray(
                    versions.map { version ->
                        buildJsonObject {
                            put("id", version.id)
                            put("version", version.version)
                            put("validationStatus", version.validationStatus)
                            put("isCurrent", version.isCurrent)
                        }
                    }
                ),
            )
            put("hasMore", hasMore)
        }

    private fun encode(message: JsonObject): String = json.encodeToString(JsonObject.serializer(), message)
}

/**
 * One page-to-host editor message. [files] is set on a save and a test run; [versionId] on a history rollback/delete;
 * [variables]/[args] on a test-run request.
 */
@Serializable
data class EditorInboundMessage(
    val type: String,
    val files: Map<String, String> = emptyMap(),
    val versionId: String = "",
    val variables: Map<String, String> = emptyMap(),
    val args: List<String> = emptyList(),
    val trigger: String? = null,
    val role: String? = null,
)
