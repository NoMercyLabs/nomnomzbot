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
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Checkbox
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.Select
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ActionDefault
import bot.nomnomz.dashboard.core.network.PlatformDefaultBlastRadius
import bot.nomnomz.dashboard.feature.admin.state.ActionDefaultEdit
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsController
import bot.nomnomz.dashboard.feature.roles.ui.LadderRungs
import bot.nomnomz.dashboard.feature.roles.ui.ladderRoleLabel
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.platform_defaults_apply
import nomnomzbot.composeapp.generated.resources.platform_defaults_cancel
import nomnomzbot.composeapp.generated.resources.platform_defaults_danger_ack
import nomnomzbot.composeapp.generated.resources.platform_defaults_edit_title
import nomnomzbot.composeapp.generated.resources.platform_defaults_pick_role
import nomnomzbot.composeapp.generated.resources.platform_defaults_use_shipped
import org.jetbrains.compose.resources.stringResource

/** One option in the role picker: a named ladder rung, or `null` for "back to the shipped default". */
private data class LevelChoice(val level: Int?)

/**
 * The editor for one action's platform default. The operator picks a role NAME (rungs at or above the
 * action's floor, or the shipped default); every pick re-fetches the server's counted blast radius, and the
 * single primary "Apply to N channels" stays disabled until that count is on screen. A dangerous action also
 * needs the acknowledgement ticked, and its apply button takes the destructive treatment instead of the accent.
 */
@Composable
internal fun ActionDefaultEditDialog(
    row: ActionDefault,
    edit: ActionDefaultEdit,
    controller: PlatformDefaultsController,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    var menuOpen: Boolean by remember { mutableStateOf(false) }

    val shippedLabel: String = stringResource(ladderRoleLabel(row.shippedDefaultLevel))
    val choices: List<LevelChoice> =
        listOf(LevelChoice(null)) +
            LadderRungs.filter { it.level >= row.floorLevel }.sortedByDescending { it.level }.map { LevelChoice(it.level) }
    val rungLabels: Map<Int, String> = LadderRungs.associate { it.level to stringResource(it.label) }
    val shippedOption: String = stringResource(Res.string.platform_defaults_use_shipped, shippedLabel)
    val preview: PlatformDefaultBlastRadius? = edit.preview

    Dialog(onDismissRequest = controller::dismissActionEdit) {
        DialogTitle(text = stringResource(Res.string.platform_defaults_edit_title, row.actionKey))
        Select(
            value = LevelChoice(edit.level),
            options = choices,
            onValueChange = { choice -> scope.launch { controller.pickActionLevel(row.actionKey, choice.level) } },
            label = stringResource(Res.string.platform_defaults_pick_role),
            optionLabel = { choice -> choice.level?.let { rungLabels[it] } ?: shippedOption },
            expanded = menuOpen,
            onExpandedChange = { menuOpen = it },
            enabled = !edit.saving,
            modifier = Modifier.fillMaxWidth(),
        )
        PlatformDefaultBlastRadiusText(preview = preview)
        if (preview?.requiresDangerConfirmation == true) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Checkbox(
                    checked = edit.dangerAcknowledged,
                    onCheckedChange = controller::acknowledgeDanger,
                    enabled = !edit.saving,
                )
                Text(
                    text = stringResource(Res.string.platform_defaults_danger_ack),
                    style = typography.sm,
                    color = tokens.popoverForeground,
                )
            }
        }
        DialogFooter {
            Button(onClick = controller::dismissActionEdit, variant = ButtonVariant.Ghost, enabled = !edit.saving) {
                Text(text = stringResource(Res.string.platform_defaults_cancel), maxLines = 1)
            }
            Button(
                onClick = { scope.launch { controller.saveActionEdit() } },
                variant = if (preview?.requiresDangerConfirmation == true) ButtonVariant.Destructive else ButtonVariant.Default,
                enabled = edit.canSave,
                loading = edit.saving,
            ) {
                Text(
                    text = stringResource(Res.string.platform_defaults_apply, preview?.channelsAffected ?: 0),
                    maxLines = 1,
                )
            }
        }
    }
}
