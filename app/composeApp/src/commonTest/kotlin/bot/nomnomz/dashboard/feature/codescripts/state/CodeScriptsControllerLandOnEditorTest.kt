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
import bot.nomnomz.dashboard.core.editor.EditorPreviewWidget
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
import bot.nomnomz.dashboard.core.network.TestTrigger
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// S-IA-SCRIPTS: Scripts opens straight into the editor. [CodeScriptsController.landOnEditor] is what the screen
// calls when it is first shown: with at least one script it launches the shared editor on the most recently
// updated one (no list page to click through), and when the editor closes the page shows the list.
class CodeScriptsControllerLandOnEditorTest {

    private val project: ProjectDto =
        ProjectDto(
            files = mapOf("index.ts" to "nnz.api.chat.send('hi');"),
            manifest = ProjectManifestDto(entry = "index.ts", framework = "script"),
        )

    private val scripts: List<CodeScriptSummary> =
        listOf(
            CodeScriptSummary(id = "old", name = "older", updatedAt = "2026-09-01T10:00:00Z"),
            CodeScriptSummary(id = "new", name = "newest", updatedAt = "2026-10-01T10:00:00Z"),
            CodeScriptSummary(id = "mid", name = "middle", updatedAt = "2026-09-15T10:00:00Z"),
        )

    @Test
    fun landing_opens_the_most_recently_updated_script_in_the_editor() = runTest {
        val api = FakeCodeScriptsApi(scripts)
        val editor = RecordingProjectEditor()
        val controller = CodeScriptsController(api, editor, StubSdkTypes)

        controller.landOnEditor(compiledMessage = "Saved.", rowTypeLabel = "Script")

        assertTrue(editor.opened, "landing on Scripts must launch the editor with no click")
        assertEquals("newest", editor.lastTitle, "the editor opens on the most recently updated script")
        assertEquals(listOf("new"), api.getCallIds, "only that script's detail was fetched")
        assertEquals(mapOf("index.ts" to "nnz.api.chat.send('hi');"), editor.lastFiles, "seeded with the script's own files")
    }

    @Test
    fun after_the_editor_closes_the_list_shows_and_the_editor_does_not_reopen() = runTest {
        val editor = RecordingProjectEditor()
        val controller = CodeScriptsController(FakeCodeScriptsApi(scripts), editor, StubSdkTypes)

        controller.landOnEditor(compiledMessage = "Saved.", rowTypeLabel = "Script")

        val state: CodeScriptsState = controller.state.value
        assertTrue(state is CodeScriptsState.Ready, "closing the editor leaves the list, got $state")
        assertEquals(3, (state as CodeScriptsState.Ready).scripts.size, "the list holds every script")
        assertEquals(1, editor.openCount, "the editor opened exactly once, never in a loop")
    }

    @Test
    fun landing_with_no_scripts_shows_the_empty_state_without_an_editor() = runTest {
        val editor = RecordingProjectEditor()
        val controller = CodeScriptsController(FakeCodeScriptsApi(emptyList()), editor, StubSdkTypes)

        controller.landOnEditor(compiledMessage = "Saved.", rowTypeLabel = "Script")

        assertTrue(controller.state.value is CodeScriptsState.Empty, "no scripts: the empty state with its add action")
        assertTrue(!editor.opened, "there is no script to open")
    }

    @Test
    fun a_pending_deep_link_wins_over_the_most_recent_script() = runTest {
        val editor = RecordingProjectEditor()
        val controller = CodeScriptsController(FakeCodeScriptsApi(scripts), editor, StubSdkTypes)

        controller.requestOpen("mid")
        controller.landOnEditor(compiledMessage = "Saved.", rowTypeLabel = "Script")

        assertEquals("middle", editor.lastTitle, "the deep-linked script opens, not the newest")
        assertEquals(1, editor.openCount, "one editor launch")
    }

    private inner class FakeCodeScriptsApi(private val items: List<CodeScriptSummary>) : CodeScriptsApi {
        val getCallIds: MutableList<String> = mutableListOf()

        override suspend fun blastRadius(id: String): ApiResult<BlastRadiusSummary> = ApiResult.Ok(BlastRadiusSummary())

        override suspend fun list(): ApiResult<List<CodeScriptSummary>> = ApiResult.Ok(items)

        override suspend fun get(id: String): ApiResult<CodeScriptDetail> {
            getCallIds += id
            val name: String = items.first { it.id == id }.name
            return ApiResult.Ok(CodeScriptDetail(id = id, name = name, isEnabled = true, language = "typescript"))
        }

        override suspend fun getProject(id: String): ApiResult<ProjectDto> = ApiResult.Ok(project)

        override suspend fun testTriggers(): ApiResult<List<TestTrigger>> = ApiResult.Ok(emptyList())

        override suspend fun testRun(id: String, body: ScriptTestRunBody): ApiResult<TestRunResult> =
            error("not exercised in this test")

        override suspend fun create(body: CreateScriptBody): ApiResult<CodeScriptDetail> =
            error("not exercised in this test")

        override suspend fun createVersion(id: String, body: CreateVersionBody): ApiResult<CodeScriptVersion> =
            ApiResult.Ok(CodeScriptVersion())

        override suspend fun putProject(id: String, project: ProjectDto): ApiResult<CodeScriptVersion> =
            ApiResult.Ok(CodeScriptVersion())

        override suspend fun listVersions(
            id: String,
            page: Int,
            pageSize: Int,
        ): ApiResult<PaginatedEnvelope<CodeScriptVersion>> = ApiResult.Ok(PaginatedEnvelope())

        override suspend fun deleteVersion(id: String, versionId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun publishVersion(id: String, versionId: String): ApiResult<CodeScriptDetail> =
            error("not exercised in this test")

        override suspend fun setEnabled(id: String, enabled: Boolean): ApiResult<Unit> = ApiResult.Ok(Unit)

        override suspend fun delete(id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    }

    private object StubSdkTypes : SdkTypesApi {
        override suspend fun types(context: String, scriptId: String?, widgetId: String?): ApiResult<String> =
            ApiResult.Ok("")
    }

    private class RecordingProjectEditor : ProjectEditorIO {
        var opened: Boolean = false
        var openCount: Int = 0
        var lastTitle: String? = null
        var lastFiles: Map<String, String>? = null

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
            opened = true
            openCount += 1
            lastTitle = title
            lastFiles = initialFiles
        }
    }
}
