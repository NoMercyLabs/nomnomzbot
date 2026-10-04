// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.codescripts.state

import bot.nomnomz.dashboard.core.editor.CompileFeedback
import bot.nomnomz.dashboard.core.editor.EditorHistory
import bot.nomnomz.dashboard.core.editor.EditorOutcome
import bot.nomnomz.dashboard.core.editor.EditorPreviewWidget
import bot.nomnomz.dashboard.core.editor.EditorTestRun
import bot.nomnomz.dashboard.core.editor.EditorTestTrigger
import bot.nomnomz.dashboard.core.editor.EditorTestRunResult
import bot.nomnomz.dashboard.core.editor.EditorTestRunTimelineEntry
import bot.nomnomz.dashboard.core.editor.ProjectEditorIO
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CapturedEffect
import bot.nomnomz.dashboard.core.network.CodeScriptDetail
import bot.nomnomz.dashboard.core.network.CodeScriptSummary
import bot.nomnomz.dashboard.core.network.CodeScriptVersion
import bot.nomnomz.dashboard.core.network.CodeScriptsApi
import bot.nomnomz.dashboard.core.network.CreateScriptBody
import bot.nomnomz.dashboard.core.network.CreateVersionBody
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.ProjectDto
import bot.nomnomz.dashboard.core.network.ProjectManifestDto
import bot.nomnomz.dashboard.core.network.ScriptTestRunBody
import bot.nomnomz.dashboard.core.network.TestTrigger
import bot.nomnomz.dashboard.core.network.SdkTypesApi
import bot.nomnomz.dashboard.core.network.BuildError
import bot.nomnomz.dashboard.core.network.SourcePosition
import bot.nomnomz.dashboard.core.network.TestRunResult
import bot.nomnomz.dashboard.core.network.TimelineEntry
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary

// Proves the code-script test-run (dry-run) flow the editor renders: with a script open, running a test surfaces
// the backend's CAPTURED result — chat output + effects — onto the editing state; a failure surfaces the reason.
// The screen is a pure projection of this state, so this proves the panel shows the real captured payload.
class CodeScriptsControllerTestRunTest {

    private val project =
        ProjectDto(
            files = mapOf("index.ts" to "nnz.api.chat.send('hi');"),
            manifest = ProjectManifestDto(entry = "index.ts", framework = "script"),
        )

    private suspend fun openedController(api: FakeCodeScriptsApi): CodeScriptsController {
        val controller = CodeScriptsController(api, NoopProjectEditor, StubSdkTypes)
        controller.load()
        controller.open("s1")
        assertTrue(controller.state.value is CodeScriptsState.Editing, "editor should be open before test-run")
        return controller
    }

    @Test
    fun test_run_surfaces_the_captured_effects_and_chat_output() = runTest {
        val captured =
            TestRunResult(
                success = true,
                error = null,
                durationMs = 12,
                hostCallCount = 2,
                capturedEffects =
                    listOf(
                        CapturedEffect(name = "chat.send", argsPreview = "read=hello"),
                        CapturedEffect(name = "storage.set", argsPreview = "written | x"),
                    ),
                chatOutput = listOf("read=hello"),
                log = listOf("Outcome: Success"),
            )
        val api = FakeCodeScriptsApi(testRunResult = ApiResult.Ok(captured))
        val controller = openedController(api)

        controller.testRun("s1", mapOf("who" to "chat"), listOf("arg1"))

        val state = controller.state.value
        assertTrue(state is CodeScriptsState.Editing)
        val editing = state as CodeScriptsState.Editing
        assertEquals(false, editing.testRunning)
        val result: TestRunResult = editing.testResult ?: error("expected a captured result")
        assertTrue(result.success)
        assertEquals(listOf("read=hello"), result.chatOutput)
        assertEquals(listOf("chat.send", "storage.set"), result.capturedEffects.map { it.name })
        // The exact args the controller sent reached the API — variables + args pass through untouched.
        assertEquals(ScriptTestRunBody(mapOf("who" to "chat"), listOf("arg1")), api.lastTestRunBody)
    }

    @Test
    fun the_editor_test_run_panel_gets_the_variables_the_script_set_and_its_console_lines() = runTest {
        val captured =
            TestRunResult(
                success = true,
                durationMs = 3,
                hostCallCount = 0,
                variablesSet = mapOf("mood" to "happy"),
                console = listOf("mood is happy", "warn: careful"),
            )
        val editor = TestRunPressingEditor()
        val controller = CodeScriptsController(FakeCodeScriptsApi(ApiResult.Ok(captured)), editor, StubSdkTypes)
        controller.load()

        controller.openAndEdit("s1", compiledMessage = "ok", displayName = "Script")

        val result: EditorTestRunResult = (editor.outcome as EditorOutcome.Ok).value
        assertEquals(mapOf("mood" to "happy"), result.variablesSet)
        assertEquals(listOf("mood is happy", "warn: careful"), result.console)
    }

    @Test
    fun the_editor_test_run_panel_gets_the_failing_line_so_the_editor_can_underline_it() = runTest {
        val captured =
            TestRunResult(
                success = false,
                error = "boom (line 3, column 5)",
                durationMs = 3,
                hostCallCount = 0,
                errorPosition = SourcePosition(file = "index.ts", line = 3, column = 5),
            )
        val editor = TestRunPressingEditor()
        val controller = CodeScriptsController(FakeCodeScriptsApi(ApiResult.Ok(captured)), editor, StubSdkTypes)
        controller.load()

        controller.openAndEdit("s1", compiledMessage = "ok", displayName = "Script")

        val result: EditorTestRunResult = (editor.outcome as EditorOutcome.Ok).value
        assertEquals(
            listOf(BuildError(code = "runtime", message = "boom (line 3, column 5)", file = "index.ts", line = 3, column = 5)),
            result.errors,
        )
    }

    @Test
    fun a_successful_test_run_hands_the_editor_no_errors() = runTest {
        val captured = TestRunResult(success = true, durationMs = 3, hostCallCount = 0)
        val editor = TestRunPressingEditor()
        val controller = CodeScriptsController(FakeCodeScriptsApi(ApiResult.Ok(captured)), editor, StubSdkTypes)
        controller.load()

        controller.openAndEdit("s1", compiledMessage = "ok", displayName = "Script")

        assertTrue((editor.outcome as EditorOutcome.Ok).value.errors.isEmpty())
    }

    @Test
    fun the_editor_test_run_panel_gets_the_server_timeline_in_order() = runTest {
        val captured =
            TestRunResult(
                success = true,
                durationMs = 3,
                hostCallCount = 1,
                timeline =
                    listOf(
                        TimelineEntry(seq = 1, kind = "console", text = "a"),
                        TimelineEntry(seq = 2, kind = "chat", text = "b"),
                    ),
            )
        val editor = TestRunPressingEditor()
        val controller = CodeScriptsController(FakeCodeScriptsApi(ApiResult.Ok(captured)), editor, StubSdkTypes)
        controller.load()

        controller.openAndEdit("s1", compiledMessage = "ok", displayName = "Script")

        val result: EditorTestRunResult = (editor.outcome as EditorOutcome.Ok).value
        assertEquals(
            listOf(EditorTestRunTimelineEntry(1, "console", "a"), EditorTestRunTimelineEntry(2, "chat", "b")),
            result.timeline,
        )
    }

    @Test
    fun the_editor_offers_the_server_trigger_samples_and_the_run_sends_the_chosen_trigger_and_role() = runTest {
        val api =
            FakeCodeScriptsApi(ApiResult.Ok(TestRunResult(success = true, durationMs = 1, hostCallCount = 0)))
        api.triggers =
            listOf(
                TestTrigger(
                    id = "FollowEvent",
                    responseKey = "chat.follow",
                    userDisplayName = "Sample Follower",
                    variables = mapOf("user" to "Sample Follower"),
                )
            )
        val editor = TestRunPressingEditor()
        editor.pressTrigger = "FollowEvent"
        editor.pressRole = "vip"
        val controller = CodeScriptsController(api, editor, StubSdkTypes)
        controller.load()

        controller.openAndEdit("s1", compiledMessage = "ok", displayName = "Script")

        assertEquals(
            listOf(EditorTestTrigger("FollowEvent", "chat.follow", mapOf("user" to "Sample Follower"))),
            editor.triggers,
        )
        assertEquals(
            ScriptTestRunBody(emptyMap(), emptyList(), trigger = "FollowEvent", role = "vip"),
            api.lastTestRunBody,
        )
    }

    @Test
    fun test_run_failure_surfaces_the_error_and_no_result() = runTest {
        val api =
            FakeCodeScriptsApi(
                testRunResult = ApiResult.Failure(ApiError(400, "VALIDATION_FAILED", "no valid version")),
            )
        val controller = openedController(api)

        controller.testRun("s1", emptyMap(), emptyList())

        val editing = controller.state.value as CodeScriptsState.Editing
        assertEquals(false, editing.testRunning)
        assertEquals("no valid version", editing.testError)
        assertEquals(null, editing.testResult)
    }

    private inner class FakeCodeScriptsApi(
        private val testRunResult: ApiResult<TestRunResult>,
    ) : CodeScriptsApi {
    // Not exercised here: the counted delete preview has its own tests (DeleteBlastRadiusDialogTest and the
    // backend's blast-radius suites). The seam is implemented so the double stays a real implementation.
    override suspend fun blastRadius(id: String): ApiResult<BlastRadiusSummary> =
        ApiResult.Ok(BlastRadiusSummary())

        var lastTestRunBody: ScriptTestRunBody? = null

        private val summary = CodeScriptSummary(id = "s1", name = "s", isEnabled = true, currentValidationStatus = "valid")
        private val detail = CodeScriptDetail(id = "s1", name = "s", isEnabled = true, language = "typescript")

        override suspend fun list(): ApiResult<List<CodeScriptSummary>> = ApiResult.Ok(listOf(summary))

        override suspend fun get(id: String): ApiResult<CodeScriptDetail> = ApiResult.Ok(detail)

        override suspend fun getProject(id: String): ApiResult<ProjectDto> = ApiResult.Ok(project)

        var triggers: List<TestTrigger> = emptyList()

        override suspend fun testTriggers(): ApiResult<List<TestTrigger>> = ApiResult.Ok(triggers)

        override suspend fun testRun(id: String, body: ScriptTestRunBody): ApiResult<TestRunResult> {
            lastTestRunBody = body
            return testRunResult
        }

        override suspend fun create(body: CreateScriptBody): ApiResult<CodeScriptDetail> = ApiResult.Ok(detail)

        override suspend fun createVersion(id: String, body: CreateVersionBody): ApiResult<CodeScriptVersion> =
            ApiResult.Ok(CodeScriptVersion())

        override suspend fun putProject(id: String, project: ProjectDto): ApiResult<CodeScriptVersion> =
            ApiResult.Ok(CodeScriptVersion())

        override suspend fun listVersions(id: String, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<CodeScriptVersion>> =
            ApiResult.Ok(PaginatedEnvelope())

        override suspend fun deleteVersion(id: String, versionId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun publishVersion(id: String, versionId: String): ApiResult<CodeScriptDetail> =
            ApiResult.Ok(detail)

        override suspend fun setEnabled(id: String, enabled: Boolean): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun delete(id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    }

    private object NoopProjectEditor : ProjectEditorIO {
        override suspend fun editAndCompile(
            title: String,
            initialFiles: Map<String, String>,
            entryPath: String,
            language: String,
            sdkTypes: String,
            sdkTypesUnavailable: Boolean,
            previewWidget: EditorPreviewWidget?,
            history: EditorHistory?,
            testRun: EditorTestRun?,
            compile: suspend (Map<String, String>) -> CompileFeedback,
        ) = Unit
    }

    // Presses the editor's Test run once while the editor is open, keeping what the panel would show.
    private class TestRunPressingEditor : ProjectEditorIO {
        var outcome: EditorOutcome<EditorTestRunResult>? = null
        var triggers: List<EditorTestTrigger> = emptyList()
        var pressTrigger: String? = null
        var pressRole: String? = null

        override suspend fun editAndCompile(
            title: String,
            initialFiles: Map<String, String>,
            entryPath: String,
            language: String,
            sdkTypes: String,
            sdkTypesUnavailable: Boolean,
            previewWidget: EditorPreviewWidget?,
            history: EditorHistory?,
            testRun: EditorTestRun?,
            compile: suspend (Map<String, String>) -> CompileFeedback,
        ) {
            triggers = testRun?.triggers.orEmpty()
            outcome = testRun?.run?.invoke(emptyMap(), emptyList(), pressTrigger, pressRole)
        }
    }

    private object StubSdkTypes : SdkTypesApi {
        override suspend fun types(context: String, scriptId: String?, widgetId: String?): ApiResult<String> =
            ApiResult.Ok("")
    }
}
