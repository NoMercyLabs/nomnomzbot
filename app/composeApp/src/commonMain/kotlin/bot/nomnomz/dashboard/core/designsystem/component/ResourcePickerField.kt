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

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.PickerKind
import bot.nomnomz.dashboard.core.network.PipelineOptionDto
import bot.nomnomz.dashboard.core.network.PipelineOptionsApi
import coil3.compose.AsyncImage
import kotlinx.coroutines.delay
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.resource_picker_empty
import nomnomzbot.composeapp.generated.resources.resource_picker_loading
import nomnomzbot.composeapp.generated.resources.resource_picker_search_placeholder
import nomnomzbot.composeapp.generated.resources.resource_picker_unavailable
import nomnomzbot.composeapp.generated.resources.search_picker_change
import nomnomzbot.composeapp.generated.resources.search_picker_selected
import org.jetbrains.compose.resources.stringResource
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel

/**
 * A backend-sourced rich picker for one [PipelineActionFieldKind] resource-picker [kind] (S-RICH-PICKERS): the
 * pipeline step form's replacement for a bare id text box on `discord_channel` / `discord_role` /
 * `twitch_user` / `reward` / `widget` / `voice` / `sound_clip` / `asset` fields. Every option renders its human
 * [PipelineOptionDto.label], the [PipelineOptionDto.secondaryText] that actually identifies it (reward cost +
 * paused state, voice locale/gender/provider, clip duration, …), and its [PipelineOptionDto.imageUrl] where the
 * source has one — never a raw id. An unselectable option stays LISTED, disabled, with its
 * [PipelineOptionDto.reason] shown (never silently missing, never selectable-then-failing). A source the
 * backend could not reach renders a distinct "could not load" message from a genuinely empty list
 * (`sourceAvailable`/`unavailableReason` on the backend result) — the two are different sentences to the
 * operator, never the same empty state.
 */
@Composable
fun ResourcePickerField(
    kind: PickerKind,
    api: PipelineOptionsApi,
    selectedId: String?,
    onSelect: (String?) -> Unit,
    modifier: Modifier = Modifier,
    label: String? = null,
    enabled: Boolean = true,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var query: String by remember(kind) { mutableStateOf("") }
    var loadState: ResourcePickerLoadState by remember(kind) { mutableStateOf(ResourcePickerLoadState.Loading) }
    var selectedOption: PipelineOptionDto? by remember(kind) { mutableStateOf(null) }

    val trimmed: String = query.trim()
    LaunchedEffect(kind, trimmed) {
        if (trimmed.isNotEmpty()) delay(300)
        loadState = ResourcePickerLoadState.Loading
        loadState =
            when (val result = api.getOptions(kind, trimmed.ifBlank { null })) {
                is ApiResult.Ok -> {
                    val dto = result.value
                    selectedId?.let { id -> dto.items.firstOrNull { it.value == id } }?.let { selectedOption = it }
                    if (!dto.sourceAvailable) {
                        ResourcePickerLoadState.Unavailable(dto.unavailableReason.orEmpty())
                    } else {
                        ResourcePickerLoadState.Loaded(dto.items)
                    }
                }
                is ApiResult.Failure -> ResourcePickerLoadState.Unavailable(result.error.message)
            }
    }

    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        val selected: PipelineOptionDto? = selectedOption
        if (selected != null) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                Text(
                    text = stringResource(Res.string.search_picker_selected, resolveRowLabel(selected.label, typeLabel = kind.name, discriminatorSource = selected.value)),
                    style = typography.sm,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
                if (enabled) {
                    TextButton(onClick = { selectedOption = null; onSelect(null) }) {
                        Text(text = stringResource(Res.string.search_picker_change), style = typography.sm, color = tokens.primary)
                    }
                }
            }
            return@Column
        }

        AppTextField(
            value = query,
            onValueChange = { query = it },
            label = label.orEmpty(),
            placeholder = stringResource(Res.string.resource_picker_search_placeholder),
            enabled = enabled,
        )

        when (val state = loadState) {
            is ResourcePickerLoadState.Loading ->
                Text(text = stringResource(Res.string.resource_picker_loading), style = typography.xs, color = tokens.mutedForeground)

            is ResourcePickerLoadState.Unavailable ->
                Text(
                    text = stringResource(Res.string.resource_picker_unavailable, state.reason),
                    style = typography.xs,
                    color = tokens.destructive,
                )

            is ResourcePickerLoadState.Loaded ->
                if (state.items.isEmpty()) {
                    Text(text = stringResource(Res.string.resource_picker_empty), style = typography.xs, color = tokens.mutedForeground)
                } else {
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                        state.items.take(8).forEach { option ->
                            ResourcePickerRow(
                                option = option,
                                typeLabel = kind.name,
                                onClick = {
                                    selectedOption = option
                                    onSelect(option.value)
                                },
                            )
                        }
                    }
                }
        }
    }
}

private sealed interface ResourcePickerLoadState {
    data object Loading : ResourcePickerLoadState

    data class Unavailable(val reason: String) : ResourcePickerLoadState

    data class Loaded(val items: List<PipelineOptionDto>) : ResourcePickerLoadState
}

@Composable
private fun ResourcePickerRow(
    option: PipelineOptionDto,
    typeLabel: String,
    onClick: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val selectable: Boolean = option.isSelectable

    Row(
        modifier =
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(spacing.s2))
                .background(tokens.popover)
                .then(if (selectable) Modifier.clickable(onClick = onClick) else Modifier)
                .padding(horizontal = spacing.s3, vertical = spacing.s2)
                .semantics { contentDescription = resolveRowLabel(option.label, typeLabel = typeLabel, discriminatorSource = option.value) },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        if (option.imageUrl != null) {
            AsyncImage(
                model = option.imageUrl,
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.size(spacing.s6).clip(RoundedCornerShape(spacing.s1)),
            )
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = resolveRowLabel(option.label, typeLabel = typeLabel, discriminatorSource = option.value),
                style = typography.sm,
                color = if (selectable) tokens.popoverForeground else tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.fillMaxWidth(),
            )
            if (!option.secondaryText.isNullOrBlank()) {
                Text(
                    text = option.secondaryText,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            if (!selectable && !option.reason.isNullOrBlank()) {
                Text(
                    text = option.reason,
                    style = typography.xs,
                    color = tokens.destructive,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }
    }
}
