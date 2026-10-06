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

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

// The bridge both hosts (web iframe, desktop native web view) run: the page's messages must reach the caller's
// compile / history / test-run with the right data, and each reply must go back in the shape editor.js reads.
class EditorBridgeSessionTest {
    private fun parse(message: String): JsonObject = Json.parseToJsonElement(message).jsonObject

    private class Harness(
        history: EditorHistory? = null,
        testRun: EditorTestRun? = null,
        sdkTypesUnavailable: Boolean = false,
        fireSamplesError: String? = null,
        private val feedback: CompileFeedback = CompileFeedback(ok = true, message = "Compiled v2"),
    ) {
        val compiled: MutableList<Map<String, String>> = mutableListOf()
        val posted: MutableList<String> = mutableListOf()
        val session: EditorBridgeSession =
            EditorBridgeSession(
                title = "Alerts",
                initialFiles = mapOf("src/App.vue" to "<template/>", "src/util.ts" to "export const a = 1"),
                entryPath = "src/App.vue",
                language = "vue",
                sdkTypes = "declare const nnz: { on(e: string): void };",
                sdkTypesUnavailable = sdkTypesUnavailable,
                previewWidget =
                    EditorPreviewWidget(
                        id = "w-1",
                        name = "Alerts",
                        settings = JsonObject(mapOf("color" to JsonPrimitive("red"))),
                        eventSubscriptions = listOf("channel.follow"),
                        fireSamples =
                            JsonObject(
                                mapOf("channel.follow" to JsonObject(mapOf("login" to JsonPrimitive("server-login"))))
                            ),
                        fireSamplesError = fireSamplesError,
                    ),
                history = history,
                testRun = testRun,
                compile = { files ->
                    compiled += files
                    feedback
                },
                post = { message -> posted += message },
            )
    }

    @Test
    fun readyIsAnsweredWithTheFullProjectAndSdkTypes() = runTest {
        val harness = Harness()

        assertTrue(harness.session.handle(EditorInboundMessage(EditorBridgeProtocol.READY)))

        val open: JsonObject = parse(harness.posted.single())
        assertEquals(EditorBridgeProtocol.OPEN, open["type"]!!.jsonPrimitive.content)
        val payload: JsonObject = open["payload"]!!.jsonObject
        assertEquals("Alerts", payload["title"]!!.jsonPrimitive.content)
        assertEquals("src/App.vue", payload["entry"]!!.jsonPrimitive.content)
        assertEquals("export const a = 1", payload["files"]!!.jsonObject["src/util.ts"]!!.jsonPrimitive.content)
        assertEquals("declare const nnz: { on(e: string): void };", payload["sdkTypes"]!!.jsonPrimitive.content)
        assertFalse(payload["sdkTypesUnavailable"]!!.jsonPrimitive.boolean)
        assertEquals("channel.follow", payload["eventSubscriptions"]!!.jsonArray.single().jsonPrimitive.content)
        // The fire bar sends the server's own sample table, the one the widget Test button fires from.
        assertEquals(
            "server-login",
            payload["fireSamples"]!!.jsonObject["channel.follow"]!!.jsonObject["login"]!!.jsonPrimitive.content,
        )
        val widget: JsonObject = payload["widget"]!!.jsonObject
        assertEquals("w-1", widget["id"]!!.jsonPrimitive.content)
        assertEquals("Alerts", widget["name"]!!.jsonPrimitive.content)
        assertEquals("red", widget["settings"]!!.jsonObject["color"]!!.jsonPrimitive.content)
        assertFalse(payload["testRunEnabled"]!!.jsonPrimitive.boolean)
        assertNull(payload["history"], "no history panel when the caller passed none")
        assertTrue(harness.compiled.isEmpty(), "opening never compiles")
    }

    @Test
    fun theOpenMessageCarriesTheSdkTypesUnavailableFlagWhenTheFetchFailed() = runTest {
        val harness = Harness(sdkTypesUnavailable = true)

        harness.session.handle(EditorInboundMessage(EditorBridgeProtocol.READY))

        val payload: JsonObject = parse(harness.posted.single())["payload"]!!.jsonObject
        assertTrue(payload["sdkTypesUnavailable"]!!.jsonPrimitive.boolean)
    }

    @Test
    fun theOpenMessageCarriesTheFireSamplesErrorWhenTheSamplesDidNotLoad() = runTest {
        val harness = Harness(fireSamplesError = "Samples did not load")

        harness.session.handle(EditorInboundMessage(EditorBridgeProtocol.READY))

        val payload: JsonObject = parse(harness.posted.single())["payload"]!!.jsonObject
        assertEquals("Samples did not load", payload["fireSamplesError"]!!.jsonPrimitive.content)
    }

    @Test
    fun theOpenMessageHasNoFireSamplesErrorWhenTheSamplesLoaded() = runTest {
        val harness = Harness()

        harness.session.handle(EditorInboundMessage(EditorBridgeProtocol.READY))

        val payload: JsonObject = parse(harness.posted.single())["payload"]!!.jsonObject
        assertNull(payload["fireSamplesError"])
    }

    @Test
    fun saveCompilesTheExactFilesAndPostsTheResultBack() = runTest {
        val harness = Harness(feedback = CompileFeedback(ok = false, message = "App.vue:3 unexpected token"))
        val edited: Map<String, String> = mapOf("src/App.vue" to "<template>v2</template>", "src/new.ts" to "x")

        val raw: String =
            """{"type":"nnz:editor:save","files":{"src/App.vue":"<template>v2</template>","src/new.ts":"x"}}"""
        assertTrue(harness.session.handle(EditorBridgeProtocol.decode(raw)!!))

        assertEquals(listOf(edited), harness.compiled)
        val reply: JsonObject = parse(harness.posted.single())
        assertEquals(EditorBridgeProtocol.COMPILED, reply["type"]!!.jsonPrimitive.content)
        assertFalse(reply["ok"]!!.jsonPrimitive.boolean)
        assertEquals("App.vue:3 unexpected token", reply["message"]!!.jsonPrimitive.content)
    }

    @Test
    fun closeEndsTheSessionWithoutPostingOrCompiling() = runTest {
        val harness = Harness()

        assertFalse(harness.session.handle(EditorInboundMessage(EditorBridgeProtocol.CLOSE)))

        assertTrue(harness.posted.isEmpty())
        assertTrue(harness.compiled.isEmpty())
    }

    @Test
    fun historyRollbackPassesTheVersionIdAndPostsTheRefreshedPage() = runTest {
        val rolledBack: MutableList<String> = mutableListOf()
        val page = EditorVersionsPage(listOf(EditorVersionSummary("v-7", 7, "valid", isCurrent = true)), hasMore = false)
        val history =
            EditorHistory(
                initialVersions = emptyList(),
                initialHasMore = false,
                loadMore = { EditorOutcome.Failed("unused") },
                rollback = { id ->
                    rolledBack += id
                    EditorOutcome.Ok(page)
                },
                delete = { EditorOutcome.Failed("unused") },
            )
        val harness = Harness(history = history)

        harness.session.handle(EditorBridgeProtocol.decode("""{"type":"nnz:editor:historyRollback","versionId":"v-7"}""")!!)

        assertEquals(listOf("v-7"), rolledBack)
        val reply: JsonObject = parse(harness.posted.single())
        assertEquals(EditorBridgeProtocol.HISTORY_PAGE, reply["type"]!!.jsonPrimitive.content)
        val row: JsonObject = reply["payload"]!!.jsonObject["versions"]!!.jsonArray.single().jsonObject
        assertEquals("v-7", row["id"]!!.jsonPrimitive.content)
        assertTrue(row["isCurrent"]!!.jsonPrimitive.boolean)
    }

    @Test
    fun historyRequestWithoutHistoryIsAVisibleErrorNotSilence() = runTest {
        val harness = Harness()

        harness.session.handle(EditorInboundMessage(EditorBridgeProtocol.HISTORY_LOAD_MORE))

        assertEquals(EditorBridgeProtocol.HISTORY_ERROR, parse(harness.posted.single())["type"]!!.jsonPrimitive.content)
    }

    @Test
    fun testRunPostsTheVariablesTheScriptSetAndItsConsoleLines() = runTest {
        val testRun =
            EditorTestRun { _, _, _, _, _ ->
                EditorOutcome.Ok(
                    EditorTestRunResult(
                        success = true,
                        durationMs = 3,
                        hostCallCount = 0,
                        error = null,
                        chatOutput = emptyList(),
                        effects = emptyList(),
                        variablesSet = mapOf("mood" to "happy"),
                        console = listOf("mood is happy"),
                    )
                )
            }
        val harness = Harness(testRun = testRun)

        harness.session.handle(EditorBridgeProtocol.decode("""{"type":"nnz:editor:testRun","variables":{},"args":[]}""")!!)

        val reply: JsonObject = parse(harness.posted.single())
        assertEquals("happy", reply["variablesSet"]!!.jsonObject["mood"]!!.jsonPrimitive.content)
        assertEquals(listOf("mood is happy"), reply["console"]!!.jsonArray.map { line -> line.jsonPrimitive.content })
    }

    @Test
    fun testRunPostsTheTimelineInOrder() = runTest {
        val testRun =
            EditorTestRun { _, _, _, _, _ ->
                EditorOutcome.Ok(
                    EditorTestRunResult(
                        success = true,
                        durationMs = 3,
                        hostCallCount = 1,
                        error = null,
                        chatOutput = listOf("b"),
                        effects = emptyList(),
                        timeline =
                            listOf(
                                EditorTestRunTimelineEntry(1, "console", "a"),
                                EditorTestRunTimelineEntry(2, "chat", "b"),
                                EditorTestRunTimelineEntry(3, "effect", "storage.set: k | c"),
                            ),
                    )
                )
            }
        val harness = Harness(testRun = testRun)

        harness.session.handle(EditorBridgeProtocol.decode("""{"type":"nnz:editor:testRun","variables":{},"args":[]}""")!!)

        val timeline = parse(harness.posted.single())["timeline"]!!.jsonArray.map { row -> row.jsonObject }
        assertEquals(listOf(1, 2, 3), timeline.map { row -> row["seq"]!!.jsonPrimitive.content.toInt() })
        assertEquals(listOf("console", "chat", "effect"), timeline.map { row -> row["kind"]!!.jsonPrimitive.content })
        assertEquals("storage.set: k | c", timeline[2]["text"]!!.jsonPrimitive.content)
    }

    @Test
    fun testRunPassesVariablesAndArgsAndPostsCapturedEffects() = runTest {
        val received: MutableList<Pair<Map<String, String>, List<String>>> = mutableListOf()
        val testRun =
            EditorTestRun { variables, args, _, _, _ ->
                received += variables to args
                EditorOutcome.Ok(
                    EditorTestRunResult(
                        success = true,
                        durationMs = 12,
                        hostCallCount = 1,
                        error = null,
                        chatOutput = listOf("hi Stoney_Eagle"),
                        effects = listOf(EditorTestRunEffect("chat.send", "\"hi\"")),
                    )
                )
            }
        val harness = Harness(testRun = testRun)

        harness.session.handle(
            EditorBridgeProtocol.decode("""{"type":"nnz:editor:testRun","variables":{"user":"Stoney_Eagle"},"args":["a","b"]}""")!!
        )

        assertEquals(listOf(mapOf("user" to "Stoney_Eagle") to listOf("a", "b")), received)
        val reply: JsonObject = parse(harness.posted.single())
        assertEquals(EditorBridgeProtocol.TEST_RUN_RESULT, reply["type"]!!.jsonPrimitive.content)
        assertTrue(reply["ok"]!!.jsonPrimitive.boolean)
        assertEquals("hi Stoney_Eagle", reply["chatOutput"]!!.jsonArray.single().jsonPrimitive.content)
        assertEquals("chat.send", reply["effects"]!!.jsonArray.single().jsonObject["name"]!!.jsonPrimitive.content)
    }

    @Test
    fun testRunPassesTheChosenTriggerAndRoleToTheCaller() = runTest {
        val received: MutableList<Pair<String?, String?>> = mutableListOf()
        val testRun =
            EditorTestRun { _, _, trigger, role, _ ->
                received += trigger to role
                EditorOutcome.Ok(
                    EditorTestRunResult(
                        success = true,
                        durationMs = 1,
                        hostCallCount = 0,
                        error = null,
                        chatOutput = emptyList(),
                        effects = emptyList(),
                    )
                )
            }
        val harness = Harness(testRun = testRun)

        harness.session.handle(
            EditorBridgeProtocol.decode(
                """{"type":"nnz:editor:testRun","variables":{},"args":[],"trigger":"x","role":"vip"}"""
            )!!
        )
        harness.session.handle(
            EditorBridgeProtocol.decode("""{"type":"nnz:editor:testRun","variables":{},"args":[]}""")!!
        )

        assertEquals(listOf<Pair<String?, String?>>("x" to "vip", null to null), received)
    }

    @Test
    fun aTestRunMessageWithFilesReachesTheTestRunWithThoseExactFiles() = runTest {
        val received: MutableList<Map<String, String>> = mutableListOf()
        val testRun =
            EditorTestRun { _, _, _, _, files ->
                received += files
                EditorOutcome.Ok(
                    EditorTestRunResult(
                        success = true,
                        durationMs = 1,
                        hostCallCount = 0,
                        error = null,
                        chatOutput = emptyList(),
                        effects = emptyList(),
                    )
                )
            }
        val harness = Harness(testRun = testRun)

        harness.session.handle(
            EditorBridgeProtocol.decode(
                """{"type":"nnz:editor:testRun","variables":{},"args":[],"files":{"index.ts":"a = 1;","lib/util.ts":"export {};"}}"""
            )!!
        )
        harness.session.handle(
            EditorBridgeProtocol.decode("""{"type":"nnz:editor:testRun","variables":{},"args":[]}""")!!
        )

        assertEquals(
            listOf(mapOf("index.ts" to "a = 1;", "lib/util.ts" to "export {};"), emptyMap()),
            received,
        )
    }

    @Test
    fun theOpenPayloadCarriesTheTriggerListAndTheLabels() = runTest {
        val testRun =
            EditorTestRun(
                triggers =
                    listOf(
                        EditorTestTrigger(
                            id = "FollowEvent",
                            label = "chat.follow",
                            variables = mapOf("user" to "Sample Follower"),
                        )
                    ),
                labels =
                    EditorTestRunLabels(
                        manual = "Manual",
                        trigger = "Trigger",
                        role = "Viewer role",
                        roles = mapOf("moderator" to "Moderator", "viewer" to "Viewer"),
                    ),
            ) { _, _, _, _, _ ->
                EditorOutcome.Failed("unused")
            }
        val harness = Harness(testRun = testRun)

        harness.session.handle(EditorInboundMessage(EditorBridgeProtocol.READY))

        val payload: JsonObject = parse(harness.posted.single())["payload"]!!.jsonObject
        assertTrue(payload["testRunEnabled"]!!.jsonPrimitive.boolean)
        val trigger: JsonObject = payload["testTriggers"]!!.jsonArray.single().jsonObject
        assertEquals("FollowEvent", trigger["id"]!!.jsonPrimitive.content)
        assertEquals("chat.follow", trigger["label"]!!.jsonPrimitive.content)
        assertEquals("Sample Follower", trigger["variables"]!!.jsonObject["user"]!!.jsonPrimitive.content)
        val labels: JsonObject = payload["labels"]!!.jsonObject
        assertEquals("Manual", labels["manual"]!!.jsonPrimitive.content)
        assertEquals("Trigger", labels["trigger"]!!.jsonPrimitive.content)
        assertEquals("Viewer role", labels["role"]!!.jsonPrimitive.content)
        assertEquals("Moderator", labels["roles"]!!.jsonObject["moderator"]!!.jsonPrimitive.content)
    }
}
