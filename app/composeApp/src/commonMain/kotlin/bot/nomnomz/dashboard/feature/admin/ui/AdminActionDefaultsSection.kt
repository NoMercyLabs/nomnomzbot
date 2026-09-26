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
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
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
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ActionDefault
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsController
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsState
import bot.nomnomz.dashboard.feature.admin.state.isDangerous
import bot.nomnomz.dashboard.feature.roles.ui.ladderRoleLabel
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.platform_defaults_action_default
import nomnomzbot.composeapp.generated.resources.platform_defaults_action_overrides
import nomnomzbot.composeapp.generated.resources.platform_defaults_action_shipped
import nomnomzbot.composeapp.generated.resources.platform_defaults_change
import nomnomzbot.composeapp.generated.resources.platform_defaults_dangerous
import nomnomzbot.composeapp.generated.resources.platform_defaults_empty
import nomnomzbot.composeapp.generated.resources.platform_defaults_filter
import org.jetbrains.compose.resources.stringResource

/**
 * The Gate-2 action defaults: one row per gateable action with the role every channel without its own
 * override must reach. Rows carry no accent — the list is reference data, and each row's single quiet
 * "Change" opens the editor, whose apply button is the one primary action of the flow. Levels render as role
 * NAMES only.
 */
@Composable
internal fun ActionDefaultsSection(state: PlatformDefaultsState, controller: PlatformDefaultsController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        AppTextField(
            value = state.actionFilter,
            onValueChange = controller::setActionFilter,
            label = stringResource(Res.string.platform_defaults_filter),
            modifier = Modifier.fillMaxWidth(),
        )
        val rows: List<ActionDefault> = state.visibleActionDefaults
        if (rows.isEmpty()) {
            Text(
                text = stringResource(Res.string.platform_defaults_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                rows.forEachIndexed { index, row ->
                    ActionDefaultRow(
                        row = row,
                        onChange = { scope.launch { controller.openActionEdit(row.actionKey) } },
                    )
                    if (index < rows.lastIndex) Separator()
                }
            }
        }
    }

    state.actionEdit?.let { edit ->
        val row: ActionDefault = state.actionDefaults.firstOrNull { it.actionKey == edit.actionKey } ?: return@let
        ActionDefaultEditDialog(row = row, edit = edit, controller = controller)
    }
}

@Composable
private fun ActionDefaultRow(row: ActionDefault, onChange: () -> Unit) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                Text(text = row.actionKey, style = typography.sm, color = tokens.cardForeground)
                if (row.isDangerous) {
                    Badge(variant = BadgeVariant.Outline) {
                        Text(text = stringResource(Res.string.platform_defaults_dangerous), style = typography.xs)
                    }
                }
            }
            row.description?.takeIf { it.isNotBlank() }?.let {
                Text(text = it, style = typography.xs, color = tokens.mutedForeground)
            }
            Text(
                text = stringResource(
                    Res.string.platform_defaults_action_default,
                    stringResource(ladderRoleLabel(row.effectiveDefaultLevel)),
                ),
                style = typography.xs,
                color = tokens.cardForeground,
            )
            if (row.platformDefaultLevel != null) {
                Text(
                    text = stringResource(
                        Res.string.platform_defaults_action_shipped,
                        stringResource(ladderRoleLabel(row.shippedDefaultLevel)),
                    ),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
            if (row.channelOverrideCount > 0) {
                Text(
                    text = stringResource(Res.string.platform_defaults_action_overrides, row.channelOverrideCount),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }
        Button(onClick = onChange, variant = ButtonVariant.Outline, size = ButtonSize.Sm) {
            Text(text = stringResource(Res.string.platform_defaults_change), maxLines = 1)
        }
    }
}
