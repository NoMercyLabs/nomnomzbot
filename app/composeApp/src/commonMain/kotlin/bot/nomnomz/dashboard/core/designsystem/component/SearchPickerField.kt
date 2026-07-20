// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import kotlinx.coroutines.delay
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.search_picker_change
import nomnomzbot.composeapp.generated.resources.search_picker_hint
import nomnomzbot.composeapp.generated.resources.search_picker_searching
import nomnomzbot.composeapp.generated.resources.search_picker_selected
import nomnomzbot.composeapp.generated.resources.viewer_picker_empty
import nomnomzbot.composeapp.generated.resources.viewer_picker_label
import nomnomzbot.composeapp.generated.resources.viewer_picker_placeholder
import org.jetbrains.compose.resources.stringResource

/**
 * One row the picker's search matched. [id] is the identifier the consuming write keys on — its meaning depends on
 * the endpoint the caller's `search` lambda hits (a platform User GUID for the `GET /users?query=` idiom, a Twitch
 * user id for `community/search`, a Twitch category id for the game search). [label] is the primary row text and
 * the name carried into the [PickerRef]; [sublabel] is the secondary line (username, etc.).
 */
data class PickerOption(
    val id: String,
    val label: String,
    val sublabel: String = "",
)

/** The option the caller committed to — [id] is the identifier the write consumes, [name] labels the selection. */
data class PickerRef(val id: String, val name: String)

/**
 * The shared debounced search picker (the idiom the Roles viewer search introduced, extracted here so moderation,
 * economy, community, the raid target, and the game/category fields all reuse one implementation instead of each
 * hand-typing a raw id). A field queries the backend through the caller-supplied [search] lambda and lists the
 * matches for selection; once one is chosen it collapses to the selected name plus a "change" affordance. The
 * [search] lambda owns WHICH endpoint is hit (and therefore which id space [PickerOption.id] lives in) — this
 * composable owns only the transient query, the 300 ms debounce, and the result list. [selected] is owned by the
 * caller.
 *
 * [label]/[placeholder]/[emptyText] default to the generic viewer wording; a category/channel picker passes its
 * own so the chrome reads correctly.
 */
@Composable
fun SearchPickerField(
    search: suspend (query: String) -> List<PickerOption>,
    selected: PickerRef?,
    onSelect: (PickerRef) -> Unit,
    onClear: () -> Unit,
    modifier: Modifier = Modifier,
    label: String? = null,
    placeholder: String? = null,
    emptyText: String? = null,
    enabled: Boolean = true,
    showAllWhenEmpty: Boolean = false,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    if (selected != null) {
        Row(
            modifier = modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.search_picker_selected, selected.name),
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (enabled) {
                TextButton(onClick = onClear) {
                    Text(
                        text = stringResource(Res.string.search_picker_change),
                        style = typography.sm,
                        color = tokens.primary,
                        maxLines = 1,
                    )
                }
            }
        }
        return
    }

    var query: String by remember { mutableStateOf("") }
    var results: List<PickerOption> by remember { mutableStateOf(emptyList()) }
    var searching: Boolean by remember { mutableStateOf(false) }

    val trimmed: String = query.trim()
    // Re-run the search when the query changes; LaunchedEffect cancels the previous run, so the 300 ms delay
    // debounces keystrokes and only the latest query hits the backend. [showAllWhenEmpty] flips the min-length
    // gate off so a local, already-loaded list (an entity picker) browses ALL rows on focus and filters as you
    // type; the remote viewer/channel pickers keep the 2-char minimum so they never enumerate on an empty query.
    LaunchedEffect(trimmed) {
        if (!showAllWhenEmpty && trimmed.length < 2) {
            results = emptyList()
            searching = false
            return@LaunchedEffect
        }
        searching = true
        delay(300)
        results = search(trimmed)
        searching = false
    }

    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        AppTextField(
            value = query,
            onValueChange = { query = it },
            label = label ?: stringResource(Res.string.viewer_picker_label),
            placeholder = placeholder ?: stringResource(Res.string.viewer_picker_placeholder),
            enabled = enabled,
            supportingText =
                if (!showAllWhenEmpty && trimmed.length < 2) {
                    stringResource(Res.string.search_picker_hint)
                } else {
                    null
                },
        )
        when {
            searching ->
                Text(
                    text = stringResource(Res.string.search_picker_searching),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            (showAllWhenEmpty || trimmed.length >= 2) && results.isEmpty() ->
                Text(
                    text = emptyText ?: stringResource(Res.string.viewer_picker_empty),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
        }
        results.take(6).forEach { option ->
            val name: String = option.label.ifBlank { option.id }
            TextButton(
                onClick = { onSelect(PickerRef(option.id, name)) },
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .semantics {
                            role = Role.Button
                            contentDescription = name
                        },
            ) {
                Column(modifier = Modifier.fillMaxWidth()) {
                    Text(
                        text = name,
                        style = typography.sm,
                        color = tokens.popoverForeground,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.fillMaxWidth(),
                    )
                    if (option.sublabel.isNotBlank()) {
                        Text(
                            text = option.sublabel,
                            style = typography.xs,
                            color = tokens.mutedForeground,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                }
            }
        }
    }
}
