// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

// Platform content's `Kind = "code_script"` authoring surface (S-ADMIN-2e, platform-admin.md §3.2-§3.3): a
// system code script is authored through the SAME shared multi-file project editor (`ProjectEditorIO`) the
// tenant-side Code Scripts page opens for its own source (`CodeScriptsController.editCode`) — the very same
// contract Widgets also opens for its own source — rather than a parallel, plainer implementation bolted
// onto this tab (the pattern `AdminContentPipelineAuthoring.kt` set for the pipeline kind's `ChainEditor`).
//
// Real compile validation happens server-side at publish time through the SAME `IScriptExecutor.CompileAsync`
// validate-on-save path a tenant's own editor save uses (`PlatformContentService.ApplyCodeScriptFanOutAsync`)
// — a platform-published script is never granted a laxer authoring path than a tenant's own, matching the
// "sandboxing is a safety property" requirement. This draft editor only captures the edited source, exactly
// like `PipelinePayloadEditor`'s draft capture never re-implements graph validation itself.

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.icon.CodeGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.editor.CompileFeedback
import bot.nomnomz.dashboard.core.editor.ProjectEditor
import bot.nomnomz.dashboard.core.editor.ProjectEditorIO
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_content_code_script_editor_title
import nomnomzbot.composeapp.generated.resources.admin_content_code_script_no_source
import nomnomzbot.composeapp.generated.resources.scripts_editor_edit_code
import org.jetbrains.compose.resources.stringResource

/** The single-file project's entry path a code-script definition's `payloadJson` maps to — there is no
 * manifest/multi-file structure at the platform-content layer, mirroring `CodeScriptContentPayload`'s
 * server-side shape (one raw source string) one-for-one. */
private const val EntryPath: String = "script.ts"

/**
 * Authors a `Kind = "code_script"` `payloadJson` through the real tenant-side code editor. [payloadJson] is
 * the current draft (`{"sourceCode": "..."}`, [CodeScriptPayloadWire]'s wire shape — the same shape the
 * backend's `CodeScriptContentPayload` parses); each "Save & Compile" in the opened editor re-derives it via
 * [onPayloadJsonChange], the same reactive-capture pattern [PipelinePayloadEditor] uses for the pipeline kind.
 *
 * [projectEditor] defaults to the real platform [ProjectEditor] but is overridable — the seam a headless test
 * uses to assert this composable drives the exact same [ProjectEditorIO] contract the tenant surface does,
 * since the real implementation opens a native overlay (a non-modal Swing dialog on desktop, an iframe on
 * web) that a Compose semantics tree cannot inspect or safely drive inside a test.
 */
@Composable
internal fun CodeScriptPayloadEditor(
    payloadJson: String,
    onPayloadJsonChange: (String) -> Unit,
    projectEditor: ProjectEditorIO = remember { ProjectEditor() },
) {
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val editorTitle: String = stringResource(Res.string.admin_content_code_script_editor_title)
    val editLabel: String = stringResource(Res.string.scripts_editor_edit_code)
    val noSourceLabel: String = stringResource(Res.string.admin_content_code_script_no_source)

    val sourceCode: String = codeScriptSourceOf(payloadJson)

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = sourceCode.ifBlank { noSourceLabel },
            style = typography.xs,
            color = tokens.mutedForeground,
            maxLines = 4,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            GlyphButton(
                icon = CodeGlyph,
                label = editLabel,
                onClick = {
                    scope.launch {
                        projectEditor.editAndCompile(
                            title = editorTitle,
                            initialFiles = mapOf(EntryPath to sourceCode),
                            entryPath = EntryPath,
                            language = "script",
                            // No SDK types / fire bar while authoring platform content — there is no channel
                            // yet to fetch `nnz.d.ts` for, exactly like `PipelinePayloadEditor`'s cross-feature
                            // pickers degrade to their best-effort empty state while authoring.
                            compile = { editedFiles ->
                                onPayloadJsonChange(codeScriptPayloadJson(editedFiles[EntryPath] ?: ""))
                                CompileFeedback(ok = true, message = editorTitle)
                            },
                        )
                    }
                },
            )
        }
    }
}

/** The `Kind = "code_script"` payload's wire shape — mirrors the backend's `CodeScriptContentPayload`
 * (`{"sourceCode": "..."}`) exactly, one field, camelCase on the wire. */
@Serializable
private data class CodeScriptPayloadWire(val sourceCode: String)

private val CodeScriptPayloadJson: Json = Json { ignoreUnknownKeys = true }

/** Parses the current draft's `sourceCode` out of its `{"sourceCode": "..."}` payload — a blank/invalid
 * payload (a brand-new definition with no draft yet) yields an empty string, the same "nothing written yet"
 * starting point [WidgetPayloadFields.Empty] gives the widget kind. */
private fun codeScriptSourceOf(payloadJson: String): String {
    if (payloadJson.isBlank()) return ""
    return runCatching { CodeScriptPayloadJson.decodeFromString(CodeScriptPayloadWire.serializer(), payloadJson) }
        .getOrNull()
        ?.sourceCode
        .orEmpty()
}

/** Encodes [sourceCode] back into the `{"sourceCode": "..."}` wire shape `CodeScriptContentPayload` parses
 * server-side. */
private fun codeScriptPayloadJson(sourceCode: String): String =
    CodeScriptPayloadJson.encodeToString(CodeScriptPayloadWire.serializer(), CodeScriptPayloadWire(sourceCode))
