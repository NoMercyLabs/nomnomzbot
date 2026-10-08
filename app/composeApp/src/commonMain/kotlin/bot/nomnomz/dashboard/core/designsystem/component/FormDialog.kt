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
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalWindowInfo
import androidx.compose.ui.unit.Dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.form_dialog_discard
import nomnomzbot.composeapp.generated.resources.form_dialog_discard_message
import nomnomzbot.composeapp.generated.resources.form_dialog_discard_title
import nomnomzbot.composeapp.generated.resources.form_dialog_keep_editing
import org.jetbrains.compose.resources.stringResource

// A form dialog never grows past this share of the window, so its title and footer always stay on screen.
private const val FormDialogMaxHeightFraction: Float = 0.9f

/**
 * The one dialog for create / edit forms (psychology spec X1 and X3).
 *
 * - The body scrolls when it is tall; the title and the footer stay visible.
 * - Save is the single filled primary. While [save] runs it shows progress and the dialog is locked. A
 *   [DialogResult.Failed] keeps the dialog open with the reason inline; [DialogResult.Done] closes it
 *   through [onDismiss]. Save stays disabled until [valid].
 * - A [dirty] form asks "Discard changes?" before it closes on Cancel, Esc or a click on the scrim. A clean
 *   form closes at once.
 * - [destructive] paints the save button red, for a form whose save removes access.
 */
@Composable
fun FormDialog(
    title: String,
    saveLabel: String,
    cancelLabel: String,
    onDismiss: () -> Unit,
    save: suspend () -> DialogResult,
    modifier: Modifier = Modifier,
    dirty: Boolean = false,
    valid: Boolean = true,
    destructive: Boolean = false,
    content: @Composable ColumnScope.() -> Unit,
) {
    val spacing = LocalSpacing.current
    val state: DialogActionState = rememberDialogActionState(onDone = onDismiss)
    var confirmingDiscard: Boolean by remember { mutableStateOf(false) }
    val requestClose: () -> Unit = {
        when {
            state.pending -> Unit
            dirty -> confirmingDiscard = true
            else -> onDismiss()
        }
    }
    val windowHeight: Dp = with(LocalDensity.current) { LocalWindowInfo.current.containerSize.height.toDp() }

    Dialog(
        onDismissRequest = requestClose,
        modifier = modifier.heightIn(max = windowHeight * FormDialogMaxHeightFraction),
        dismissOnBackPress = !state.pending,
        dismissOnClickOutside = !state.pending,
    ) {
        DialogTitle(text = title)
        Column(
            modifier = Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
            content = content,
        )
        state.failure?.let { failure: DialogResult.Failed -> DialogActionError(failure) }
        DialogFooter {
            DialogActionDismiss(state = state, label = cancelLabel, onDismiss = requestClose)
            DialogActionConfirm(state = state, label = saveLabel, enabled = valid, destructive = destructive, action = save)
        }
    }

    if (confirmingDiscard) {
        ConfirmDialog(
            title = stringResource(Res.string.form_dialog_discard_title),
            message = stringResource(Res.string.form_dialog_discard_message),
            confirmLabel = stringResource(Res.string.form_dialog_discard),
            dismissLabel = stringResource(Res.string.form_dialog_keep_editing),
            onConfirm = onDismiss,
            onDismiss = { confirmingDiscard = false },
            destructive = true,
        )
    }
}
