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

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens

// The one confirmation dialog for the whole dashboard. Every irreversible action (delete a command, ban a
// viewer, clear a queue, disconnect an integration) routes its confirm step through here, so the wording,
// the destructive-red affirmative, and the cancel affordance are identical everywhere (design-system rule:
// destructive actions MUST confirm). Caller owns the open/closed state; this only renders when shown.
// Built on the DS [AlertDialog] (shadcn Dialog) — no raw Material3.
@Composable
fun ConfirmDialog(
    title: String,
    message: String,
    confirmLabel: String,
    dismissLabel: String,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
    destructive: Boolean = false,
) {
    val tokens = LocalTokens.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = { Text(text = message) },
        confirmButton = {
            // Destructive confirms get a solid red button (shadcn destructive variant) — the earlier ghost
            // text button showed only a white-opacity hover background, reading as non-destructive. The
            // affirmative for an irreversible action must be unmistakably red. Non-destructive stays a text button.
            if (destructive) {
                Button(onClick = onConfirm, variant = ButtonVariant.Destructive) {
                    Text(text = confirmLabel, maxLines = 1)
                }
            } else {
                TextButton(onClick = onConfirm) {
                    Text(text = confirmLabel, color = tokens.primary, maxLines = 1)
                }
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = dismissLabel, color = tokens.mutedForeground, maxLines = 1)
            }
        },
    )
}
