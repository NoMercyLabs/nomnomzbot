// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.platformtemplates.ui

// The one "install a platform template" dialog every feature page opens for its own kind. The page supplies
// how to load and install (bound to its channel) plus the kind's preset: what an install replaces, and
// whether the template needs one of the channel's pipelines. Install copies the template; the dialog shows
// the consequence before the single primary action enables.

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogDescription
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
import bot.nomnomz.dashboard.core.designsystem.component.RadioGroup
import bot.nomnomz.dashboard.core.designsystem.component.Select
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.InstalledPlatformTemplate
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PlatformTemplate
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.platform_templates_cancel
import nomnomzbot.composeapp.generated.resources.platform_templates_choose_pipeline
import nomnomzbot.composeapp.generated.resources.platform_templates_description
import nomnomzbot.composeapp.generated.resources.platform_templates_empty
import nomnomzbot.composeapp.generated.resources.platform_templates_install
import nomnomzbot.composeapp.generated.resources.platform_templates_installing
import nomnomzbot.composeapp.generated.resources.platform_templates_loading
import nomnomzbot.composeapp.generated.resources.platform_templates_pick_pipeline
import nomnomzbot.composeapp.generated.resources.platform_templates_title
import org.jetbrains.compose.resources.stringResource

/**
 * @param loadTemplates the published templates of the page's kind, for the page's channel.
 * @param install installs [PlatformTemplate] into the page's channel, binding the chosen pipeline id if any.
 * @param consequence what installing this template changes in the channel, shown before install enables.
 * @param needsPipeline whether this template runs a pipeline the channel must pick from [pipelines].
 */
@Composable
fun PlatformTemplatesDialog(
    loadTemplates: suspend () -> ApiResult<List<PlatformTemplate>>,
    install: suspend (PlatformTemplate, String?) -> ApiResult<InstalledPlatformTemplate>,
    onInstalled: (InstalledPlatformTemplate) -> Unit,
    onDismiss: () -> Unit,
    consequence: @Composable (PlatformTemplate) -> String?,
    pipelines: List<PipelineSummary> = emptyList(),
    needsPipeline: (PlatformTemplate) -> Boolean = { false },
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    var templates: List<PlatformTemplate>? by remember { mutableStateOf(null) }
    var selected: PlatformTemplate? by remember { mutableStateOf(null) }
    var pipelineId: String? by remember { mutableStateOf(null) }
    var pipelineMenuOpen: Boolean by remember { mutableStateOf(false) }
    var submitting: Boolean by remember { mutableStateOf(false) }
    var error: String? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) {
        when (val result: ApiResult<List<PlatformTemplate>> = loadTemplates()) {
            is ApiResult.Ok -> {
                templates = result.value
                selected = result.value.firstOrNull()
            }
            is ApiResult.Failure -> {
                templates = emptyList()
                error = result.error.message
            }
        }
    }

    val current: PlatformTemplate? = selected
    val pipelineMissing: Boolean = current != null && needsPipeline(current) && pipelineId == null

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.platform_templates_title))
        DialogDescription(text = stringResource(Res.string.platform_templates_description))

        val loaded: List<PlatformTemplate>? = templates
        when {
            loaded == null ->
                Text(
                    text = stringResource(Res.string.platform_templates_loading),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            loaded.isEmpty() && error == null ->
                Text(
                    text = stringResource(Res.string.platform_templates_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            loaded.isNotEmpty() ->
                RadioGroup(
                    options = loaded,
                    selected = current,
                    onSelectedChange = {
                        selected = it
                        pipelineId = null
                        error = null
                    },
                    label = { it.displayName },
                )
        }

        if (current != null) {
            Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                current.description?.takeIf { it.isNotBlank() }?.let {
                    Text(text = it, style = typography.sm, color = tokens.foreground)
                }
                consequence(current)?.let {
                    Text(text = it, style = typography.sm, color = tokens.mutedForeground)
                }
                if (needsPipeline(current)) {
                    Select(
                        value = pipelines.firstOrNull { it.id == pipelineId },
                        options = pipelines,
                        onValueChange = { pipelineId = it.id },
                        label = stringResource(Res.string.platform_templates_pick_pipeline),
                        optionLabel = { it.name },
                        expanded = pipelineMenuOpen,
                        onExpandedChange = { pipelineMenuOpen = it },
                        placeholder = stringResource(Res.string.platform_templates_choose_pipeline),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
        }

        error?.let { InlineError(message = it) }

        DialogFooter {
            Button(onClick = onDismiss, variant = ButtonVariant.Ghost) {
                Text(text = stringResource(Res.string.platform_templates_cancel))
            }
            Button(
                onClick = {
                    val template: PlatformTemplate = current ?: return@Button
                    submitting = true
                    error = null
                    scope.launch {
                        when (val result: ApiResult<InstalledPlatformTemplate> = install(template, pipelineId)) {
                            is ApiResult.Ok -> onInstalled(result.value)
                            is ApiResult.Failure -> error = result.error.message
                        }
                        submitting = false
                    }
                },
                enabled = current != null && !pipelineMissing && !submitting,
            ) {
                Text(
                    text =
                        if (submitting) stringResource(Res.string.platform_templates_installing)
                        else stringResource(Res.string.platform_templates_install),
                )
            }
        }
    }
}
