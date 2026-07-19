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
import bot.nomnomz.dashboard.core.network.ProjectDto
import bot.nomnomz.dashboard.core.network.ProjectManifestDto
import bot.nomnomz.dashboard.core.network.ScriptTestRunBody
import bot.nomnomz.dashboard.core.network.SdkTypesApi
import bot.nomnomz.dashboard.core.network.TestRunResult
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

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
        var lastTestRunBody: ScriptTestRunBody? = null

        private val summary = CodeScriptSummary(id = "s1", name = "s", isEnabled = true, currentValidationStatus = "valid")
        private val detail = CodeScriptDetail(id = "s1", name = "s", isEnabled = true, language = "typescript")

        override suspend fun list(): ApiResult<List<CodeScriptSummary>> = ApiResult.Ok(listOf(summary))

        override suspend fun get(id: String): ApiResult<CodeScriptDetail> = ApiResult.Ok(detail)

        override suspend fun getProject(id: String): ApiResult<ProjectDto> = ApiResult.Ok(project)

        override suspend fun testRun(id: String, body: ScriptTestRunBody): ApiResult<TestRunResult> {
            lastTestRunBody = body
            return testRunResult
        }

        override suspend fun create(body: CreateScriptBody): ApiResult<CodeScriptSummary> = ApiResult.Ok(summary)

        override suspend fun createVersion(id: String, body: CreateVersionBody): ApiResult<CodeScriptVersion> =
            ApiResult.Ok(CodeScriptVersion())

        override suspend fun putProject(id: String, project: ProjectDto): ApiResult<CodeScriptVersion> =
            ApiResult.Ok(CodeScriptVersion())

        override suspend fun listVersions(id: String): ApiResult<List<CodeScriptVersion>> = ApiResult.Ok(emptyList())

        override suspend fun publishVersion(id: String, versionId: String): ApiResult<CodeScriptSummary> =
            ApiResult.Ok(summary)

        override suspend fun setEnabled(id: String, enabled: Boolean): ApiResult<CodeScriptSummary> = ApiResult.Ok(summary)

        override suspend fun delete(id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    }

    private object NoopProjectEditor : ProjectEditorIO {
        override suspend fun editAndCompile(
            title: String,
            initialFiles: Map<String, String>,
            entryPath: String,
            language: String,
            sdkTypes: String,
            compile: suspend (Map<String, String>) -> CompileFeedback,
        ) = Unit
    }

    private object StubSdkTypes : SdkTypesApi {
        override suspend fun types(context: String): ApiResult<String> = ApiResult.Ok("")
    }
}
