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
import bot.nomnomz.dashboard.core.editor.EditorTestRun
import bot.nomnomz.dashboard.core.editor.ProjectEditorIO
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
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
import bot.nomnomz.dashboard.core.network.SdkTypesApi
import bot.nomnomz.dashboard.core.network.TestRunResult
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// The owner's observation (S-OBS-10): "the code-scripts intermediate landing page is pointless — it should
// navigate straight to the script editor instead of an in-between page." Opening an EXISTING script already goes
// straight to the editor (S-CODE-COLLAPSE, see ScriptRow.onOpen -> openAndEdit). The one remaining hop was
// CREATING one: the old [CodeScriptsController.create] only reloaded the list, forcing the operator back onto the
// list to click the new row's edit action themselves. This proves create() now launches the shared project editor
// directly on the freshly created script — no intermediate list screen in between.
class CodeScriptsControllerCreateOpensEditorTest {

    @Test
    fun create_opens_the_new_scripts_editor_directly_with_no_intermediate_list_screen() = runTest {
        val api = FakeCodeScriptsApi()
        val editor = RecordingProjectEditor()
        val controller = CodeScriptsController(api, editor, StubSdkTypes)

        // Land on the plain list first, exactly as the screen's LaunchedEffect(Unit) { controller.load() } does.
        controller.load()
        assertTrue(controller.state.value is CodeScriptsState.Ready, "starts on the list")

        controller.create(
            name = "My New Script",
            description = null,
            sourceCode = "nnz.api.chat.send('hi');",
            compiledMessage = "Saved.",
            rowTypeLabel = "Script",
        )

        assertTrue(editor.opened, "creating a script must launch the real editor directly, not just reload the list")
        assertEquals("My New Script", editor.lastTitle, "the editor opens titled with the script the operator just named")
        assertEquals(listOf("new-1"), api.getCallIds, "the newly created script's own detail was fetched to seed the editor")
        assertEquals(listOf("new-1"), api.getProjectCallIds, "the newly created script's own project was fetched to seed the editor")
    }

    @Test
    fun create_failure_surfaces_over_the_kept_list_without_opening_the_editor() = runTest {
        val api = FakeCodeScriptsApi(createShouldFail = true)
        val editor = RecordingProjectEditor()
        val controller = CodeScriptsController(api, editor, StubSdkTypes)

        controller.load()
        controller.create(
            name = "My New Script",
            description = null,
            sourceCode = "nnz.api.chat.send('hi');",
            compiledMessage = "Saved.",
            rowTypeLabel = "Script",
        )

        assertTrue(!editor.opened, "a failed create must never open the editor")
        assertTrue(controller.state.value is CodeScriptsState.Ready, "the list stays put on a failed create")
    }

    private inner class FakeCodeScriptsApi(
        private val createShouldFail: Boolean = false,
    ) : CodeScriptsApi {
        val getCallIds: MutableList<String> = mutableListOf()
        val getProjectCallIds: MutableList<String> = mutableListOf()

        private val project =
            ProjectDto(
                files = mapOf("index.ts" to "nnz.api.chat.send('hi');"),
                manifest = ProjectManifestDto(entry = "index.ts", framework = "script"),
            )

        override suspend fun blastRadius(id: String): ApiResult<BlastRadiusSummary> =
            ApiResult.Ok(BlastRadiusSummary())

        // A non-empty list so the controller lands in Ready (not Empty) before create() runs — the scenario this
        // test is actually about: an operator with existing scripts creating one more.
        override suspend fun list(): ApiResult<List<CodeScriptSummary>> =
            ApiResult.Ok(listOf(CodeScriptSummary(id = "existing-1", name = "Existing", isEnabled = true, currentValidationStatus = "valid")))

        override suspend fun get(id: String): ApiResult<CodeScriptDetail> {
            getCallIds += id
            return ApiResult.Ok(CodeScriptDetail(id = id, name = "My New Script", isEnabled = true, language = "typescript"))
        }

        override suspend fun getProject(id: String): ApiResult<ProjectDto> {
            getProjectCallIds += id
            return ApiResult.Ok(project)
        }

        override suspend fun testRun(id: String, body: ScriptTestRunBody): ApiResult<TestRunResult> =
            error("not exercised in this test")

        override suspend fun create(body: CreateScriptBody): ApiResult<CodeScriptDetail> =
            if (createShouldFail) {
                ApiResult.Failure(bot.nomnomz.dashboard.core.network.ApiError(400, "VALIDATION_FAILED", "bad script"))
            } else {
                ApiResult.Ok(CodeScriptDetail(id = "new-1", name = body.name, isEnabled = true, language = "typescript"))
            }

        override suspend fun createVersion(id: String, body: CreateVersionBody): ApiResult<CodeScriptVersion> =
            ApiResult.Ok(CodeScriptVersion())

        override suspend fun putProject(id: String, project: ProjectDto): ApiResult<CodeScriptVersion> =
            ApiResult.Ok(CodeScriptVersion())

        override suspend fun listVersions(id: String, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<CodeScriptVersion>> =
            ApiResult.Ok(PaginatedEnvelope())

        override suspend fun deleteVersion(id: String, versionId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun publishVersion(id: String, versionId: String): ApiResult<CodeScriptDetail> =
            ApiResult.Ok(CodeScriptDetail(id = id, name = "My New Script", isEnabled = true, language = "typescript"))

        override suspend fun setEnabled(id: String, enabled: Boolean): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun delete(id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    }

    private class RecordingProjectEditor : ProjectEditorIO {
        var opened: Boolean = false
        var lastTitle: String? = null

        override suspend fun editAndCompile(
            title: String,
            initialFiles: Map<String, String>,
            entryPath: String,
            language: String,
            sdkTypes: String,
            eventSubscriptions: List<String>,
            history: EditorHistory?,
            testRun: EditorTestRun?,
            compile: suspend (Map<String, String>) -> CompileFeedback,
        ) {
            opened = true
            lastTitle = title
        }
    }

    private object StubSdkTypes : SdkTypesApi {
        override suspend fun types(context: String): ApiResult<String> = ApiResult.Ok("")
    }
}
