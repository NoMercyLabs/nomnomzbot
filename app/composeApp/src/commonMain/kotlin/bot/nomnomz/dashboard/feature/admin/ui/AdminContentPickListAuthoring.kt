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

// Authoring form for the `pick_list` platform-template kind: the same fields the channel's own pick-list dialog
// edits (name, description, entries). The name is the `{list.pick.<name>}` key chat templates use, so the form
// says which characters it may hold; the server enforces the rest on save.

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.PickListTemplatePayload
import bot.nomnomz.dashboard.core.network.PlatformTemplateJson
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_content_pick_list_name_hint
import nomnomzbot.composeapp.generated.resources.picklists_dialog_add_item
import nomnomzbot.composeapp.generated.resources.picklists_dialog_description_label
import nomnomzbot.composeapp.generated.resources.picklists_dialog_item_placeholder
import nomnomzbot.composeapp.generated.resources.picklists_dialog_items_label
import nomnomzbot.composeapp.generated.resources.picklists_dialog_name_label
import nomnomzbot.composeapp.generated.resources.picklists_dialog_remove_item
import org.jetbrains.compose.resources.stringResource

private val PickListNamePattern: Regex = Regex("^[A-Za-z0-9_-]+$")

/** The form's editable state. */
internal data class PickListTemplateFields(
    val name: String = "",
    val description: String = "",
    val items: List<String> = listOf(""),
) {
    /** The payload the server receives; blank entries are dropped. */
    fun toPayload(): PickListTemplatePayload =
        PickListTemplatePayload(
            name = name.trim(),
            description = description.trim().ifEmpty { null },
            items = items.map { it.trim() }.filter { it.isNotEmpty() },
        )

    fun toPayloadJson(): String = PlatformTemplateJson.encodeToString(PickListTemplatePayload.serializer(), toPayload())

    /** True when the name is a valid `{list.pick.<name>}` key and at least one entry is filled in. */
    fun isNameValid(): Boolean = PickListNamePattern.matches(name.trim())

    fun isComplete(): Boolean = isNameValid() && toPayload().items.isNotEmpty()

    companion object {
        /** Reads an existing draft back into the form; a payload of another shape starts from an empty form. */
        fun fromPayloadJson(payloadJson: String): PickListTemplateFields {
            val payload: PickListTemplatePayload =
                runCatching { PlatformTemplateJson.decodeFromString(PickListTemplatePayload.serializer(), payloadJson) }
                    .getOrNull() ?: return PickListTemplateFields()
            return PickListTemplateFields(
                name = payload.name,
                description = payload.description.orEmpty(),
                items = payload.items.ifEmpty { listOf("") },
            )
        }
    }
}

@Composable
internal fun PickListPayloadEditor(fields: PickListTemplateFields, onFieldsChange: (PickListTemplateFields) -> Unit) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val itemLabel: String = stringResource(Res.string.picklists_dialog_item_placeholder)
    val removeLabel: String = stringResource(Res.string.picklists_dialog_remove_item)
    val nameHint: String = stringResource(Res.string.admin_content_pick_list_name_hint)

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        AppTextField(
            value = fields.name,
            onValueChange = { onFieldsChange(fields.copy(name = it)) },
            label = stringResource(Res.string.picklists_dialog_name_label),
            isError = fields.name.isNotBlank() && !fields.isNameValid(),
            supportingText = nameHint,
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = fields.description,
            onValueChange = { onFieldsChange(fields.copy(description = it)) },
            label = stringResource(Res.string.picklists_dialog_description_label),
            modifier = Modifier.fillMaxWidth(),
        )
        Text(
            text = stringResource(Res.string.picklists_dialog_items_label),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        fields.items.forEachIndexed { index, item ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                AppTextField(
                    value = item,
                    onValueChange = { updated ->
                        onFieldsChange(fields.copy(items = fields.items.toMutableList().also { it[index] = updated }))
                    },
                    label = itemLabel,
                    modifier = Modifier.weight(1f),
                )
                if (fields.items.size > 1) {
                    GlyphButton(
                        icon = TrashGlyph,
                        label = removeLabel,
                        onClick = { onFieldsChange(fields.copy(items = fields.items.filterIndexed { i, _ -> i != index })) },
                        tint = tokens.destructive,
                    )
                }
            }
        }
        TextButton(onClick = { onFieldsChange(fields.copy(items = fields.items + "")) }) {
            Text(text = stringResource(Res.string.picklists_dialog_add_item), color = tokens.primary)
        }
    }
}
