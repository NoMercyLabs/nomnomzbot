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
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcon
import bot.nomnomz.dashboard.core.designsystem.icon.CodeGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.i18n.resolveSchemaString
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.TemplateHelperContext
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.template_helpers_dialog_description
import nomnomzbot.composeapp.generated.resources.template_helpers_dialog_empty
import nomnomzbot.composeapp.generated.resources.template_helpers_dialog_error
import nomnomzbot.composeapp.generated.resources.template_helpers_dialog_search_placeholder
import nomnomzbot.composeapp.generated.resources.template_helpers_dialog_title
import nomnomzbot.composeapp.generated.resources.template_helpers_link_label
import org.jetbrains.compose.resources.stringResource

// The shared "All helpers" entry point (S043) — replaces the per-screen chip scroller (`VariableChips` in
// EventResponsesScreen.kt) with ONE reusable link + dialog wired into every template text field: commands,
// event responses, timers, pipelines, chat triggers, giveaways, Discord, rewards. A click opens
// [TemplateHelpersDialog], which fetches the full valid helper set for [context] from the backend registry
// (`GET /api/v1/templates/helpers?context=`, S042) — never a hand-duplicated list — and lets the streamer
// search, browse by namespace, and insert a placeholder into the field that opened it.
//
// [onInsert] receives the literal `{key}` token to append to the field's current value (the same single-
// brace shape `TemplateResolver`'s regex actually matches — see `TemplateResolver.cs:1314`,
// `\{([^{}]+)\}` — the `{{double}}` form in earlier docs was never what the resolver reads).
@Composable
fun TemplateHelpersLink(
    context: TemplateHelperContext,
    api: TemplateHelpersApi,
    onInsert: (String) -> Unit,
    modifier: Modifier = Modifier,
    eventType: String? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    var open: Boolean by remember { mutableStateOf(false) }

    // Muted, not accent: this is a field affordance that sits beside the form's real primary action
    // (Save / Add / Create). Painting both at full accent put two focal points in one group and diluted
    // the one control the streamer is actually meant to press. The dialog it opens carries the accent.
    TextButton(onClick = { open = true }, modifier = modifier) {
        AppIcon(CodeGlyph, contentDescription = null, tint = tokens.mutedForeground, size = spacing.s4)
        Text(
            text = stringResource(Res.string.template_helpers_link_label),
            color = tokens.mutedForeground,
        )
    }

    if (open) {
        TemplateHelpersDialog(
            context = context,
            api = api,
            eventType = eventType,
            onInsert = { token ->
                onInsert(token)
                open = false
            },
            onDismissRequest = { open = false },
        )
    }
}

/** Loading/success/failure of the one `helpers(context)` call a [TemplateHelpersDialog] makes on open. */
private sealed interface HelperLoadState {
    data object Loading : HelperLoadState

    data class Loaded(val helpers: List<TemplateHelperDto>) : HelperLoadState

    data object Failed : HelperLoadState
}

/**
 * The "All helpers" popup itself: search box + namespace-grouped, click-to-insert list. Exposed directly
 * (not just via [TemplateHelpersLink]) so a call site that wants a non-default trigger affordance can still
 * reuse the exact same dialog body.
 */
@Composable
fun TemplateHelpersDialog(
    context: TemplateHelperContext,
    api: TemplateHelpersApi,
    onInsert: (String) -> Unit,
    onDismissRequest: () -> Unit,
    eventType: String? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var loadState: HelperLoadState by remember(context, eventType) { mutableStateOf(HelperLoadState.Loading) }
    var query: String by remember { mutableStateOf("") }

    LaunchedEffect(context, eventType) {
        loadState =
            when (val result = api.helpers(context, eventType)) {
                is ApiResult.Ok -> HelperLoadState.Loaded(result.value)
                is ApiResult.Failure -> HelperLoadState.Failed
            }
    }

    Dialog(onDismissRequest = onDismissRequest) {
        DialogTitle(text = stringResource(Res.string.template_helpers_dialog_title))
        DialogDescription(text = stringResource(Res.string.template_helpers_dialog_description))

        AppTextField(
            value = query,
            onValueChange = { query = it },
            label = stringResource(Res.string.template_helpers_dialog_search_placeholder),
            modifier = Modifier.fillMaxWidth(),
        )

        when (val state = loadState) {
            is HelperLoadState.Loading ->
                Text(text = "…", style = typography.sm, color = tokens.mutedForeground)

            is HelperLoadState.Failed ->
                Text(
                    text = stringResource(Res.string.template_helpers_dialog_error),
                    style = typography.sm,
                    color = tokens.destructive,
                )

            is HelperLoadState.Loaded -> {
                val grouped: List<Pair<String, List<TemplateHelperDto>>> =
                    groupTemplateHelpers(filterTemplateHelpers(state.helpers, query))

                if (grouped.isEmpty()) {
                    Text(
                        text = stringResource(Res.string.template_helpers_dialog_empty),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                } else {
                    LazyColumn(
                        modifier = Modifier.fillMaxWidth().heightIn(max = 420.dp),
                        verticalArrangement = Arrangement.spacedBy(spacing.s3),
                    ) {
                        grouped.forEach { (namespace, helpers) ->
                            item(key = "header:$namespace") {
                                Text(
                                    text = namespace,
                                    style = typography.xs.copy(fontWeight = FontWeight.SemiBold),
                                    color = tokens.mutedForeground,
                                )
                            }
                            items(helpers, key = { "helper:${it.key}" }) { helper ->
                                TemplateHelperRow(helper = helper, onInsert = { onInsert("{${helper.key}}") })
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun TemplateHelperRow(helper: TemplateHelperDto, onInsert: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    TextButton(onClick = onInsert, modifier = Modifier.fillMaxWidth()) {
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s0_5), modifier = Modifier.fillMaxWidth()) {
            Text(text = "{${helper.key}}", style = typography.sm, color = tokens.primary)
            Text(
                text = resolveSchemaString(helper.descriptionKey),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
    }
}

/**
 * Case-insensitive filter over a helper's key AND its resolved description key text — pure so it is unit-
 * testable without Compose. An empty/blank [query] returns every helper unchanged.
 */
internal fun filterTemplateHelpers(
    helpers: List<TemplateHelperDto>,
    query: String,
): List<TemplateHelperDto> {
    val needle: String = query.trim().lowercase()
    if (needle.isEmpty()) return helpers
    return helpers.filter { it.key.lowercase().contains(needle) }
}

/**
 * Groups helpers by namespace — the segment before the first `.` in [TemplateHelperDto.key], or the whole
 * key when it carries none (e.g. `botname`, `date`) grouped under itself. Groups are sorted alphabetically;
 * within a group, helpers keep the registry's declared order (pure, unit-testable without Compose).
 */
internal fun groupTemplateHelpers(
    helpers: List<TemplateHelperDto>,
): List<Pair<String, List<TemplateHelperDto>>> =
    helpers
        .groupBy { it.key.substringBefore('.', missingDelimiterValue = it.key) }
        .entries
        .sortedBy { it.key }
        .map { (namespace, entries) -> namespace to entries }
