// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.streaminfo.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.component.PickerRef
import bot.nomnomz.dashboard.core.designsystem.component.SearchPickerField
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.feature.settings.ui.SaveBar
import bot.nomnomz.dashboard.feature.streaminfo.state.StreamInfoState
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.category_picker_empty
import nomnomzbot.composeapp.generated.resources.category_picker_label
import nomnomzbot.composeapp.generated.resources.category_picker_placeholder
import nomnomzbot.composeapp.generated.resources.settings_label_tags
import nomnomzbot.composeapp.generated.resources.settings_label_title
import nomnomzbot.composeapp.generated.resources.settings_tags_hint
import nomnomzbot.composeapp.generated.resources.settings_title_invalid
import org.jetbrains.compose.resources.stringResource

// Tags are edited as a single comma-separated field — the natural shape for a short list — and normalized to the
// list the API wants: split on commas, trim each, drop blanks. Round-trips with [joinTags] so an unchanged field
// is byte-for-byte the loaded list and the "differs from loaded" check below stays exact.
private fun parseTags(raw: String): List<String> =
    raw.split(',').map { it.trim() }.filter { it.isNotEmpty() }

private fun joinTags(tags: List<String>): String = tags.joinToString(", ")

// The one stream info editor (title, category, tags and the save bar). The Stream page "Info and presets" tab and
// the Home prep dialog both render it, so a title change from either place goes through the same
// StreamInfoController.save and reaches the same channel stream write. The caller owns the data: [state] carries
// the loaded info plus the save signals, [manage] says whether the caller may edit, [onSave] persists.
@Composable
fun StreamInfoEditor(
    state: StreamInfoState.Ready,
    manage: ManageDecision,
    onSearchCategories: suspend (String) -> List<PickerOption>,
    onSave: (title: String, gameName: String, tags: List<String>) -> Unit,
    modifier: Modifier = Modifier,
    framed: Boolean = true,
) {
    val spacing = LocalSpacing.current
    val loaded: StreamInfo = state.info

    // Local editable form, re-seeded whenever new info loads (initial load or a successful save). Holding it
    // here keeps the controller a thin persistence boundary; `remember(loaded)` resets every field to the saved
    // baseline so the "differs from loaded" check below is exact.
    var title: String by remember(loaded) { mutableStateOf(loaded.title.orEmpty()) }
    // The category picker owns a PickerRef; the stream update writes only the NAME, so the current game is seeded
    // as PickerRef(name, name) — the id is unused on this write.
    var game: PickerRef? by remember(loaded) {
        mutableStateOf(loaded.gameName?.takeIf { it.isNotBlank() }?.let { PickerRef(it, it) })
    }
    var tagsText: String by remember(loaded) { mutableStateOf(joinTags(loaded.tags)) }

    val gameName: String = game?.name.orEmpty()
    val editedTags: List<String> = parseTags(tagsText)

    // Twitch rejects an empty broadcast title, so the form requires a non-blank title before it lets a save go.
    val titleValid: Boolean = title.isNotBlank()

    // Save is offered only when the form is valid AND actually differs from the saved baseline.
    val dirty: Boolean =
        title != loaded.title.orEmpty() ||
            gameName != loaded.gameName.orEmpty() ||
            editedTags != loaded.tags
    val canSave: Boolean = titleValid && dirty && !state.saving

    // Editing the broadcast metadata gates at the caller's [manage]; below it the fields go read-only with the
    // reason via the gated SaveBar.
    val enabled: Boolean = !state.saving && manage.isAllowed

    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        val fields: @Composable ColumnScope.() -> Unit = {
            AppTextField(
                value = title,
                onValueChange = { title = it },
                enabled = enabled,
                isError = !titleValid,
                modifier = Modifier.fillMaxWidth(),
                label = stringResource(Res.string.settings_label_title),
                errorText = stringResource(Res.string.settings_title_invalid),
            )
            SearchPickerField(
                search = onSearchCategories,
                selected = game,
                onSelect = { game = it },
                onClear = { game = null },
                label = stringResource(Res.string.category_picker_label),
                placeholder = stringResource(Res.string.category_picker_placeholder),
                emptyText = stringResource(Res.string.category_picker_empty),
                modifier = Modifier.fillMaxWidth(),
                enabled = enabled,
            )
            AppTextField(
                value = tagsText,
                onValueChange = { tagsText = it },
                enabled = enabled,
                modifier = Modifier.fillMaxWidth(),
                label = stringResource(Res.string.settings_label_tags),
                supportingText = stringResource(Res.string.settings_tags_hint),
            )
        }
        // Framed on a page; unframed in a dialog, whose own surface is the container (a card of the same radius
        // inside it would break the concentric radius).
        if (framed) {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(
                    modifier = Modifier.padding(spacing.s4),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                    content = fields,
                )
            }
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s4), content = fields)
        }

        SaveBar(
            saving = state.saving,
            justSaved = state.justSaved,
            saveError = state.saveError,
            manage = manage,
            canSave = canSave && manage.isAllowed,
            onSave = { onSave(title.trim(), gameName.trim(), editedTags) },
        )
    }
}
