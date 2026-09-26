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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.PlatformDefaultBlastRadius
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsController
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsState
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.platform_defaults_explain
import nomnomzbot.composeapp.generated.resources.platform_defaults_preview_counted
import nomnomzbot.composeapp.generated.resources.platform_defaults_preview_keeping
import nomnomzbot.composeapp.generated.resources.platform_defaults_preview_loading
import nomnomzbot.composeapp.generated.resources.platform_defaults_preview_none
import nomnomzbot.composeapp.generated.resources.platform_defaults_section_permissions
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

/** The platform-default families the tab edits, one segment each. */
internal enum class PlatformDefaultsSection(val label: StringResource) {
    Permissions(Res.string.platform_defaults_section_permissions),
}

/**
 * The admin "Platform defaults" tab (plan item A4): the runtime-editable defaults every channel without its
 * own setting follows. The lead line states the consequence once, because one edit here moves every channel
 * that never chose for itself; a segmented control switches between the default families.
 */
@Composable
internal fun PlatformDefaultsTab(controller: PlatformDefaultsController) {
    val state: PlatformDefaultsState by controller.state.collectAsStateWithLifecycle()
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    var section: PlatformDefaultsSection by remember { mutableStateOf(PlatformDefaultsSection.Permissions) }

    LaunchedEffect(section) {
        when (section) {
            PlatformDefaultsSection.Permissions -> if (!state.actionsLoaded) controller.loadActionDefaults()
        }
    }

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Text(
            text = stringResource(Res.string.platform_defaults_explain),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        TabsList {
            PlatformDefaultsSection.entries.forEach { entry ->
                TabsTrigger(selected = entry == section, onClick = { section = entry }) {
                    Text(text = stringResource(entry.label), maxLines = 1)
                }
            }
        }
        when (section) {
            PlatformDefaultsSection.Permissions -> ActionDefaultsSection(state = state, controller = controller)
        }
    }
}

/**
 * The counted blast radius of the pending edit, shown in every family's editor before its save arms: who
 * changes right now (with a recognisable sample) and who keeps their own setting.
 */
@Composable
internal fun PlatformDefaultBlastRadiusText(preview: PlatformDefaultBlastRadius?) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(
            text = when {
                preview == null -> stringResource(Res.string.platform_defaults_preview_loading)
                preview.channelsAffected <= 0 -> stringResource(Res.string.platform_defaults_preview_none)
                else -> stringResource(
                    Res.string.platform_defaults_preview_counted,
                    preview.channelsAffected,
                    preview.sampleChannelNames.joinToString(", "),
                )
            },
            style = typography.sm,
            color = tokens.popoverForeground,
        )
        if (preview != null && preview.channelsKeepingOwnSetting > 0) {
            Text(
                text = stringResource(Res.string.platform_defaults_preview_keeping, preview.channelsKeepingOwnSetting),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
    }
}
