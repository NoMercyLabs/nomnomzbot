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

// Platform content's `Kind = "widget"` authoring surface (S-ADMIN-2f, platform-admin.md §3.2-§3.3): a
// system widget's Vue SFC source is authored through the SAME shared multi-file project editor
// (`ProjectEditorIO`) the tenant-side Widgets page opens for its own source (`WidgetsController.openEditor`)
// — the very same contract `AdminContentCodeScriptAuthoring.kt` already wired for the code-script kind —
// rather than the raw, undifferentiated JSON text field this kind carried until now (the last of the four
// platform-content kinds to close the "raw text where a proper editor belongs" gap S-ADMIN-2 opened).
//
// Only the Vue source field moves onto the shared editor. The default-settings JSON field and the default
// event-subscriptions list editor are untouched — a system widget's settings schema and subscriptions still
// author and round-trip exactly as before; this slice fixes the ONE odd-one-out field, not the whole kind.
//
// Real compile validation happens server-side at publish time through the SAME widget rebuild path a
// tenant's own editor save uses (`PlatformContentService.ApplyWidgetFanOutAsync`) — a platform-published
// widget is never granted a laxer authoring path than a tenant's own. This draft editor only captures the
// edited source, exactly like `CodeScriptPayloadEditor`'s draft capture never re-implements the build itself.

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
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
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_content_code_script_no_source
import nomnomzbot.composeapp.generated.resources.admin_content_widget_event_add
import nomnomzbot.composeapp.generated.resources.admin_content_widget_event_add_label
import nomnomzbot.composeapp.generated.resources.admin_content_widget_event_remove
import nomnomzbot.composeapp.generated.resources.admin_content_widget_events_empty
import nomnomzbot.composeapp.generated.resources.admin_content_widget_events_label
import nomnomzbot.composeapp.generated.resources.admin_content_widget_settings_label
import nomnomzbot.composeapp.generated.resources.admin_content_widget_source_label
import nomnomzbot.composeapp.generated.resources.widgets_edit_code_action_short
import org.jetbrains.compose.resources.stringResource

/** The single-file project's entry path a widget definition's `payloadJson.sourceCode` maps to — there is
 * no manifest/multi-file structure at the platform-content layer, mirroring the "index.vue" entry a fresh
 * tenant vue widget project seeds one-for-one (`WidgetsController.entryFileName("vue")`). */
private const val EntryPath: String = "index.vue"

/**
 * The widget-kind authoring fields (S-ADMIN-2c-c, source editing closed by S-ADMIN-2f): the single-file Vue
 * SFC source, the default settings a fresh tenant install seeds, and the default event subscriptions — the
 * same three ingredients `WidgetContentPayload` carries server-side.
 */
internal data class WidgetPayloadFields(
    val sourceCode: String,
    val settingsJson: String,
    val eventSubscriptions: List<String>,
) {
    fun toPayloadJson(): String {
        val settings: JsonObject = runCatching { WidgetPayloadJson.parseToJsonElement(settingsJson).jsonObject }
            .getOrDefault(JsonObject(emptyMap()))
        val payload = WidgetPayloadWire(
            sourceCode = sourceCode,
            defaultSettings = settings,
            defaultEventSubscriptions = eventSubscriptions,
        )
        return WidgetPayloadJson.encodeToString(WidgetPayloadWire.serializer(), payload)
    }

    companion object {
        val Empty: WidgetPayloadFields = WidgetPayloadFields(sourceCode = "", settingsJson = "{}", eventSubscriptions = emptyList())

        /** Parses an existing `payloadJson` string back into editable fields. A payload that isn't the
         * expected widget shape (e.g. a fresh definition with no draft yet) falls back to [Empty] rather
         * than crashing the dialog. */
        fun fromPayloadJson(payloadJson: String): WidgetPayloadFields {
            if (payloadJson.isBlank()) return Empty
            val wire: WidgetPayloadWire = runCatching {
                WidgetPayloadJson.decodeFromString(WidgetPayloadWire.serializer(), payloadJson)
            }.getOrNull() ?: return Empty
            return WidgetPayloadFields(
                sourceCode = wire.sourceCode,
                settingsJson = WidgetPayloadJson.encodeToString(JsonObject.serializer(), wire.defaultSettings),
                eventSubscriptions = wire.defaultEventSubscriptions,
            )
        }
    }
}

/** The wire shape of a widget `payloadJson` (backend `WidgetContentPayload`). */
@Serializable
private data class WidgetPayloadWire(
    val sourceCode: String,
    val defaultSettings: JsonObject = JsonObject(emptyMap()),
    val defaultEventSubscriptions: List<String> = emptyList(),
)

private val WidgetPayloadJson: Json = Json {
    ignoreUnknownKeys = true
    isLenient = true
    prettyPrint = true
}

/**
 * Authors a `Kind = "widget"` `payloadJson` through the real tenant-side Vue editor for [fields.sourceCode];
 * the settings schema and event subscriptions stay on their existing raw-JSON field and list editor
 * respectively — [onFieldsChange] is called reactively for every one of the three, the same reactive-capture
 * pattern [CodeScriptPayloadEditor] uses for the code-script kind.
 *
 * [projectEditor] defaults to the real platform [ProjectEditor] but is overridable — the seam a headless
 * test uses to assert this composable drives the exact same [ProjectEditorIO] contract the tenant surface
 * does, since the real implementation opens a native overlay (a non-modal Swing dialog on desktop, an
 * iframe on web) that a Compose semantics tree cannot inspect or safely drive inside a test.
 */
@Composable
internal fun WidgetPayloadEditor(
    fields: WidgetPayloadFields,
    onFieldsChange: (WidgetPayloadFields) -> Unit,
    projectEditor: ProjectEditorIO = remember { ProjectEditor() },
) {
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val editorTitle: String = stringResource(Res.string.admin_content_widget_source_label)
    val editLabel: String = stringResource(Res.string.widgets_edit_code_action_short)
    val noSourceLabel: String = stringResource(Res.string.admin_content_code_script_no_source)

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Text(
            text = fields.sourceCode.ifBlank { noSourceLabel },
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
                            initialFiles = mapOf(EntryPath to fields.sourceCode),
                            entryPath = EntryPath,
                            language = "vue",
                            // No SDK types / fire bar while authoring platform content — there is no channel
                            // yet to fetch `nnz.d.ts` for or a persisted overlay to fire test events at,
                            // exactly like `PipelinePayloadEditor`'s cross-feature pickers degrade to their
                            // best-effort empty state while authoring.
                            compile = { editedFiles ->
                                onFieldsChange(fields.copy(sourceCode = editedFiles[EntryPath] ?: ""))
                                CompileFeedback(ok = true, message = editorTitle)
                            },
                        )
                    }
                },
            )
        }
        JsonPayloadField(
            value = fields.settingsJson,
            onValueChange = { onFieldsChange(fields.copy(settingsJson = it)) },
            label = stringResource(Res.string.admin_content_widget_settings_label),
        )
        EventSubscriptionsEditor(
            subscriptions = fields.eventSubscriptions,
            onSubscriptionsChange = { onFieldsChange(fields.copy(eventSubscriptions = it)) },
        )
    }
}

/** An add/remove list editor for a widget's default event subscriptions — a flat list of event names, each
 * removable individually, plus a text field + button to add one more. */
@Composable
private fun EventSubscriptionsEditor(subscriptions: List<String>, onSubscriptionsChange: (List<String>) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    var newSubscription: String by remember { mutableStateOf("") }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(
            text = stringResource(Res.string.admin_content_widget_events_label),
            style = typography.sm,
            color = tokens.foreground,
        )
        if (subscriptions.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_content_widget_events_empty))
        } else {
            subscriptions.forEachIndexed { index, subscription ->
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(text = subscription, style = typography.sm, color = tokens.cardForeground)
                    Button(
                        onClick = { onSubscriptionsChange(subscriptions.filterIndexed { i, _ -> i != index }) },
                        variant = ButtonVariant.DestructiveGhost,
                        size = ButtonSize.Sm,
                    ) {
                        Text(text = stringResource(Res.string.admin_content_widget_event_remove), style = typography.xs)
                    }
                }
            }
        }
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            AppTextField(
                value = newSubscription,
                onValueChange = { newSubscription = it },
                label = stringResource(Res.string.admin_content_widget_event_add_label),
                modifier = Modifier.weight(1f),
            )
            Button(
                onClick = {
                    val trimmed = newSubscription.trim()
                    if (trimmed.isNotEmpty() && trimmed !in subscriptions) {
                        onSubscriptionsChange(subscriptions + trimmed)
                        newSubscription = ""
                    }
                },
                variant = ButtonVariant.Outline,
                size = ButtonSize.Sm,
                enabled = newSubscription.isNotBlank(),
            ) {
                Text(text = stringResource(Res.string.admin_content_widget_event_add))
            }
        }
    }
}
