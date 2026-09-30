// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.ui

import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.feature.settings.state.ToneChange
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.personality_change_cancel
import nomnomzbot.composeapp.generated.resources.personality_change_confirm
import nomnomzbot.composeapp.generated.resources.personality_change_counts
import nomnomzbot.composeapp.generated.resources.personality_change_counts_unknown
import nomnomzbot.composeapp.generated.resources.personality_change_title
import org.jetbrains.compose.resources.stringResource

// The consequence of a tone change, shown BEFORE it is saved: how many active event responses follow the platform
// default (they change voice) and how many keep their own text (they do not). The counts come from the channel's
// real event responses; when they could not be read the dialog says so and still states the rule.
@Composable
internal fun ToneChangeDialog(
    change: ToneChange,
    toneLabel: String,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
) {
    val following: Int? = change.following
    val own: Int? = change.own
    ConfirmDialog(
        title = stringResource(Res.string.personality_change_title, toneLabel),
        message =
            if (following != null && own != null) {
                stringResource(Res.string.personality_change_counts, following, own)
            } else {
                stringResource(Res.string.personality_change_counts_unknown)
            },
        confirmLabel = stringResource(Res.string.personality_change_confirm),
        dismissLabel = stringResource(Res.string.personality_change_cancel),
        onConfirm = onConfirm,
        onDismiss = onDismiss,
    )
}
