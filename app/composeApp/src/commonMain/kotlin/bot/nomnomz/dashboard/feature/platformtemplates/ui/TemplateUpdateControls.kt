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

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogDescription
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
import bot.nomnomz.dashboard.core.network.PlatformTemplateUpdate
import bot.nomnomz.dashboard.feature.platformtemplates.state.TemplateUpdateConfirm
import bot.nomnomz.dashboard.feature.platformtemplates.state.TemplateUpdatesController
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.template_update_action
import nomnomzbot.composeapp.generated.resources.template_update_action_label
import nomnomzbot.composeapp.generated.resources.template_update_badge
import nomnomzbot.composeapp.generated.resources.template_update_cancel
import nomnomzbot.composeapp.generated.resources.template_update_confirm
import nomnomzbot.composeapp.generated.resources.template_update_replaces_edits
import nomnomzbot.composeapp.generated.resources.template_update_title
import org.jetbrains.compose.resources.stringResource

// The row half of "template updates reach tenants": a neutral badge that says a newer template version exists,
// and an outline Update action that never competes with the page's own primary action.

/** Which rows have an update and which one is updating now, read by a page's row list. */
data class TemplateUpdateRows(
    val updates: Map<String, PlatformTemplateUpdate> = emptyMap(),
    val applying: String? = null,
) {
    fun hasUpdate(rowId: String): Boolean = rowId in updates

    fun isApplying(rowId: String): Boolean = applying == rowId
}

/** The live [TemplateUpdateRows] of [controller]; no rows when the page has no update flow wired. */
@Composable
fun templateUpdateRows(controller: TemplateUpdatesController?): TemplateUpdateRows {
    if (controller == null) return TemplateUpdateRows()
    val updates: Map<String, PlatformTemplateUpdate> by controller.updates.collectAsStateWithLifecycle()
    val applying: String? by controller.applying.collectAsStateWithLifecycle()
    return TemplateUpdateRows(updates, applying)
}

/** Marks a row whose template has a newer published version. Secondary, so it informs without shouting. */
@Composable
fun TemplateUpdateBadge() {
    Badge(variant = BadgeVariant.Secondary) {
        Text(text = stringResource(Res.string.template_update_badge), maxLines = 1)
    }
}

/** The row's Update action for [displayName]. Busy while its own update is in flight. */
@Composable
fun TemplateUpdateAction(displayName: String, busy: Boolean, enabled: Boolean, onClick: () -> Unit) {
    val label: String = stringResource(Res.string.template_update_action_label, displayName)
    Button(
        onClick = onClick,
        variant = ButtonVariant.Outline,
        size = ButtonSize.Sm,
        enabled = enabled,
        loading = busy,
        modifier = Modifier.testTag("template-update-action").semantics { contentDescription = label },
    ) {
        Text(text = stringResource(Res.string.template_update_action), maxLines = 1)
    }
}

/**
 * The confirm for updating a copy the channel changed. It names the consequence before anything is written: the
 * update replaces the channel's changes. Replace is the one primary action, in the destructive treatment. A failed
 * update keeps the dialog open with the reason.
 */
@Composable
fun TemplateUpdateConfirmDialog(controller: TemplateUpdatesController, displayName: (PlatformTemplateUpdate) -> String) {
    val pending: TemplateUpdateConfirm? by controller.pendingConfirm.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val current: TemplateUpdateConfirm = pending ?: return

    Dialog(onDismissRequest = controller::dismiss) {
        DialogTitle(
            text = stringResource(Res.string.template_update_title, displayName(current.update)),
        )
        DialogDescription(
            text = stringResource(Res.string.template_update_replaces_edits, current.update.currentVersion),
        )
        current.error?.let { detail -> InlineError(message = detail) }
        DialogFooter {
            Button(onClick = controller::dismiss, variant = ButtonVariant.Outline) {
                Text(text = stringResource(Res.string.template_update_cancel), maxLines = 1)
            }
            Button(
                onClick = { scope.launch { controller.confirm() } },
                variant = ButtonVariant.Destructive,
                enabled = !current.applying,
                loading = current.applying,
                modifier = Modifier.testTag("template-update-confirm"),
            ) {
                Text(text = stringResource(Res.string.template_update_confirm), maxLines = 1)
            }
        }
    }
}
