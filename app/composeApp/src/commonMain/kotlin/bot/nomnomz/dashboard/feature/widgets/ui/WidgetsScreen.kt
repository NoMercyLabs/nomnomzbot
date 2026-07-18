// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import bot.nomnomz.dashboard.core.designsystem.component.Button
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.GalleryItemSummary
import bot.nomnomz.dashboard.core.network.GalleryListRequest
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.core.network.WidgetTemplate
import bot.nomnomz.dashboard.core.network.WidgetVersionSummary
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.widgets.state.WidgetEditorMessages
import bot.nomnomz.dashboard.feature.widgets.state.WidgetsController
import bot.nomnomz.dashboard.feature.widgets.state.WidgetsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.widgets_action_error
import nomnomzbot.composeapp.generated.resources.widgets_badge_disabled
import nomnomzbot.composeapp.generated.resources.widgets_badge_enabled
import nomnomzbot.composeapp.generated.resources.widgets_compile_success
import nomnomzbot.composeapp.generated.resources.widgets_delete_action
import nomnomzbot.composeapp.generated.resources.widgets_delete_cancel
import nomnomzbot.composeapp.generated.resources.widgets_delete_confirm
import nomnomzbot.composeapp.generated.resources.widgets_delete_message
import nomnomzbot.composeapp.generated.resources.widgets_delete_title
import nomnomzbot.composeapp.generated.resources.widgets_empty
import nomnomzbot.composeapp.generated.resources.widgets_error
import nomnomzbot.composeapp.generated.resources.widgets_loading
import nomnomzbot.composeapp.generated.resources.widgets_retry
import nomnomzbot.composeapp.generated.resources.shell_nav_overlays
import nomnomzbot.composeapp.generated.resources.widgets_submit_action
import nomnomzbot.composeapp.generated.resources.widgets_review_action
import nomnomzbot.composeapp.generated.resources.widgets_subtitle
import nomnomzbot.composeapp.generated.resources.widgets_toggle_action
import nomnomzbot.composeapp.generated.resources.widgets_url_copied
import nomnomzbot.composeapp.generated.resources.widgets_url_copy
import nomnomzbot.composeapp.generated.resources.widgets_url_label
import nomnomzbot.composeapp.generated.resources.widgets_clone_action
import nomnomzbot.composeapp.generated.resources.widgets_clone_action_short
import nomnomzbot.composeapp.generated.resources.widgets_edit_code_action
import nomnomzbot.composeapp.generated.resources.widgets_edit_code_action_short
import nomnomzbot.composeapp.generated.resources.widgets_clone_dismiss
import nomnomzbot.composeapp.generated.resources.widgets_clone_message
import nomnomzbot.composeapp.generated.resources.widgets_create_action
import nomnomzbot.composeapp.generated.resources.widgets_create_confirm
import nomnomzbot.composeapp.generated.resources.widgets_create_dismiss
import nomnomzbot.composeapp.generated.resources.widgets_create_framework
import nomnomzbot.composeapp.generated.resources.widgets_create_name
import nomnomzbot.composeapp.generated.resources.widgets_create_name_required
import nomnomzbot.composeapp.generated.resources.widgets_create_template
import nomnomzbot.composeapp.generated.resources.widgets_create_template_blank
import nomnomzbot.composeapp.generated.resources.widgets_create_template_loading
import nomnomzbot.composeapp.generated.resources.widgets_create_title
import nomnomzbot.composeapp.generated.resources.widgets_rename_action
import nomnomzbot.composeapp.generated.resources.widgets_rename_action_short
import nomnomzbot.composeapp.generated.resources.widgets_rename_confirm
import nomnomzbot.composeapp.generated.resources.widgets_rename_dismiss
import nomnomzbot.composeapp.generated.resources.widgets_rename_name
import nomnomzbot.composeapp.generated.resources.widgets_rename_title
import nomnomzbot.composeapp.generated.resources.widgets_rollback_confirm
import nomnomzbot.composeapp.generated.resources.widgets_rollback_dismiss
import nomnomzbot.composeapp.generated.resources.widgets_rollback_message
import nomnomzbot.composeapp.generated.resources.widgets_rollback_title
import nomnomzbot.composeapp.generated.resources.widgets_url_missing
import nomnomzbot.composeapp.generated.resources.widgets_versions_action
import nomnomzbot.composeapp.generated.resources.widgets_versions_action_short
import nomnomzbot.composeapp.generated.resources.widgets_versions_active
import nomnomzbot.composeapp.generated.resources.widgets_versions_close
import nomnomzbot.composeapp.generated.resources.widgets_versions_empty
import nomnomzbot.composeapp.generated.resources.widgets_versions_error
import nomnomzbot.composeapp.generated.resources.widgets_versions_loading
import nomnomzbot.composeapp.generated.resources.widgets_versions_row
import nomnomzbot.composeapp.generated.resources.widgets_versions_title
import nomnomzbot.composeapp.generated.resources.widgets_rollback_action
import nomnomzbot.composeapp.generated.resources.widgets_gallery_action
import nomnomzbot.composeapp.generated.resources.widgets_gallery_clone_action
import nomnomzbot.composeapp.generated.resources.widgets_gallery_clone_action_short
import nomnomzbot.composeapp.generated.resources.widgets_gallery_close
import nomnomzbot.composeapp.generated.resources.widgets_gallery_empty
import nomnomzbot.composeapp.generated.resources.widgets_gallery_error
import nomnomzbot.composeapp.generated.resources.widgets_gallery_filter_all
import nomnomzbot.composeapp.generated.resources.widgets_gallery_install_action
import nomnomzbot.composeapp.generated.resources.widgets_gallery_install_action_short
import nomnomzbot.composeapp.generated.resources.widgets_gallery_install_count
import nomnomzbot.composeapp.generated.resources.widgets_gallery_loading
import nomnomzbot.composeapp.generated.resources.widgets_gallery_title
import nomnomzbot.composeapp.generated.resources.widgets_gallery_trust_first_party
import nomnomzbot.composeapp.generated.resources.widgets_gallery_trust_unverified
import nomnomzbot.composeapp.generated.resources.widgets_gallery_trust_verified
import org.jetbrains.compose.resources.stringResource

// The Overlays page (frontend-ia.md §3, Stream group): the channel's OBS browser-source overlay widgets, all
// real data from [WidgetsController]. The screen is a pure projection of the controller's state; it loads on
// first composition. The core value of the page is each overlay's browser-source URL, shown in a copyable chip
// the operator pastes into an OBS browser source. Each overlay can be enabled/disabled inline, renamed, cloned,
// edited (the compile-on-save code editor), rolled back to a past version, or deleted — deletion is destructive
// (its browser-source URL stops resolving once gone), so it confirms first.
@Composable
fun WidgetsScreen(controller: WidgetsController, role: ManagementRole?, isReviewer: Boolean = false) {
    val state: WidgetsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    // One decision for the whole page: Overlays gates every write control at its single Editor manage floor
    // (frontend-ia.md §3, Stream group). A caller below it still sees each overlay and can copy its
    // browser-source URL, but every write control (enable/disable, rename, clone, edit-code, rollback, delete)
    // renders disabled with "Requires Editor" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Widgets)

    // Localized editor feedback strings, resolved here (a Composable) and threaded into the controller's compile
    // callback — the controller is a plain state holder with no access to Compose resources.
    val editorMessages =
        WidgetEditorMessages(compiled = stringResource(Res.string.widgets_compile_success))

    var pendingDelete: PendingDelete? by remember { mutableStateOf(null) }
    var pendingRename: WidgetSummary? by remember { mutableStateOf(null) }
    var pendingClone: WidgetSummary? by remember { mutableStateOf(null) }
    var pendingVersions: WidgetSummary? by remember { mutableStateOf(null) }
    var pendingRollback: PendingRollback? by remember { mutableStateOf(null) }
    var showCreateDialog: Boolean by remember { mutableStateOf(false) }
    var showGalleryDialog: Boolean by remember { mutableStateOf(false) }
    var showSubmitDialog: Boolean by remember { mutableStateOf(false) }
    var showReviewQueue: Boolean by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(
            title = stringResource(Res.string.shell_nav_overlays),
            subtitle = stringResource(Res.string.widgets_subtitle),
        ) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                // Browsing the gallery is a public read, so it stays ungated; the Install / Clone controls inside
                // the dialog carry the Editor write gate.
                Button(
                    onClick = { showGalleryDialog = true },
                    variant = ButtonVariant.Outline,
                ) {
                    Text(stringResource(Res.string.widgets_gallery_action))
                }
                // Submitting a community widget is open to any signed-in user (it lands in the review queue,
                // not the live catalogue) — the backend validates the SHA/URL and gates the eventual verify.
                Button(
                    onClick = { showSubmitDialog = true },
                    variant = ButtonVariant.Outline,
                ) {
                    Text(stringResource(Res.string.widgets_submit_action))
                }
                // The review queue only appears for a platform reviewer (gallery:review). The backend is the real
                // gate — a non-reviewer's list is scoped to their own items and every review write 403s.
                if (isReviewer) {
                    Button(
                        onClick = { showReviewQueue = true },
                        variant = ButtonVariant.Outline,
                    ) {
                        Text(stringResource(Res.string.widgets_review_action))
                    }
                }
                ManageGate(manage) {
                    Button(onClick = { showCreateDialog = true }) {
                        Text(stringResource(Res.string.widgets_create_action))
                    }
                }
            }
        }

        when (val current: WidgetsState = state) {
            is WidgetsState.Loading -> CenteredMessage(stringResource(Res.string.widgets_loading))
            is WidgetsState.Empty -> CenteredMessage(stringResource(Res.string.widgets_empty))
            is WidgetsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is WidgetsState.Ready ->
                ReadyContent(
                    widgets = current.widgets,
                    actionError = current.actionError,
                    manage = manage,
                    onToggle = { widget, enabled ->
                        scope.launch { controller.toggleWidget(widget.id, enabled) }
                    },
                    onDelete = { widget -> pendingDelete = PendingDelete(widget.id, widget.name) },
                    onRename = { widget -> pendingRename = widget },
                    onClone = { widget -> pendingClone = widget },
                    onEditCode = { widget -> scope.launch { controller.editWidgetCode(widget, editorMessages) } },
                    onVersions = { widget -> pendingVersions = widget },
                )
        }
    }

    pendingDelete?.let { target ->
        ConfirmDialog(
            title = stringResource(Res.string.widgets_delete_title),
            message = stringResource(Res.string.widgets_delete_message, target.name),
            confirmLabel = stringResource(Res.string.widgets_delete_confirm),
            dismissLabel = stringResource(Res.string.widgets_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteWidget(target.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }

    pendingRename?.let { widget ->
        RenameWidgetDialog(
            currentName = widget.name,
            onConfirm = { newName ->
                pendingRename = null
                scope.launch { controller.renameWidget(widget.id, newName) }
            },
            onDismiss = { pendingRename = null },
        )
    }

    pendingClone?.let { widget ->
        ConfirmDialog(
            title = stringResource(Res.string.widgets_clone_action, widget.name),
            message = stringResource(Res.string.widgets_clone_message, widget.name),
            confirmLabel = stringResource(Res.string.widgets_clone_action_short),
            dismissLabel = stringResource(Res.string.widgets_clone_dismiss),
            destructive = false,
            onConfirm = {
                pendingClone = null
                scope.launch { controller.cloneWidget(widget.id) }
            },
            onDismiss = { pendingClone = null },
        )
    }

    pendingVersions?.let { widget ->
        WidgetVersionsDialog(
            widget = widget,
            manage = manage,
            loadVersions = { controller.listVersions(widget.id) },
            onRollback = { version ->
                pendingVersions = null
                pendingRollback = PendingRollback(widget.id, version)
            },
            onDismiss = { pendingVersions = null },
        )
    }

    pendingRollback?.let { target ->
        ConfirmDialog(
            title = stringResource(Res.string.widgets_rollback_title),
            message = stringResource(Res.string.widgets_rollback_message, target.version.versionNumber.toString()),
            confirmLabel = stringResource(Res.string.widgets_rollback_confirm),
            dismissLabel = stringResource(Res.string.widgets_rollback_dismiss),
            destructive = false,
            onConfirm = {
                pendingRollback = null
                scope.launch { controller.rollbackVersion(target.widgetId, target.version.id) }
            },
            onDismiss = { pendingRollback = null },
        )
    }

    if (showCreateDialog) {
        CreateWidgetDialog(
            loadTemplates = { controller.listTemplates() },
            onConfirm = { name, framework, seedSource ->
                showCreateDialog = false
                scope.launch { controller.createWidget(name, framework, seedSource, editorMessages) }
            },
            onDismiss = { showCreateDialog = false },
        )
    }

    if (showGalleryDialog) {
        GalleryBrowseDialog(
            manage = manage,
            loadGallery = { request -> controller.listGallery(request) },
            onInstall = { item ->
                showGalleryDialog = false
                scope.launch { controller.installFromGallery(item.id) }
            },
            onClone = { item ->
                showGalleryDialog = false
                scope.launch { controller.cloneFromGallery(item.id, editorMessages) }
            },
            onDismiss = { showGalleryDialog = false },
        )
    }

    if (showSubmitDialog) {
        WidgetSubmitDialog(
            submit = { body -> controller.submitToGallery(body) },
            onDismiss = { showSubmitDialog = false },
        )
    }

    if (showReviewQueue) {
        WidgetReviewSheet(
            loadQueue = { status -> controller.listReviewQueue(status) },
            loadDetail = { id -> controller.galleryItemDetail(id) },
            onReview = { id, body -> controller.reviewGalleryItem(id, body) },
            onPin = { id, body -> controller.pinGalleryItem(id, body) },
            onDismiss = { showReviewQueue = false },
        )
    }
}

// The list-bearing content: an optional write-failure banner over the overlay rows. The header (title +
// subtitle) lives on the screen so it shows in every state, matching the integrations page.
@Composable
private fun ReadyContent(
    widgets: List<WidgetSummary>,
    actionError: String?,
    manage: ManageDecision,
    onToggle: (WidgetSummary, Boolean) -> Unit,
    onDelete: (WidgetSummary) -> Unit,
    onRename: (WidgetSummary) -> Unit,
    onClone: (WidgetSummary) -> Unit,
    onEditCode: (WidgetSummary) -> Unit,
    onVersions: (WidgetSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        actionError?.let { ActionErrorBanner(message = stringResource(Res.string.widgets_action_error, it)) }
        WidgetList(
            widgets = widgets,
            manage = manage,
            onToggle = onToggle,
            onDelete = onDelete,
            onRename = onRename,
            onClone = onClone,
            onEditCode = onEditCode,
            onVersions = onVersions,
        )
    }
}

@Composable
private fun WidgetList(
    widgets: List<WidgetSummary>,
    manage: ManageDecision,
    onToggle: (WidgetSummary, Boolean) -> Unit,
    onDelete: (WidgetSummary) -> Unit,
    onRename: (WidgetSummary) -> Unit,
    onClone: (WidgetSummary) -> Unit,
    onEditCode: (WidgetSummary) -> Unit,
    onVersions: (WidgetSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    Card(modifier = Modifier.fillMaxSize()) {
        LazyColumn(
            modifier = Modifier.fillMaxWidth(),
            contentPadding = PaddingValues(vertical = spacing.s1),
        ) {
            itemsIndexed(items = widgets, key = { _, widget -> widget.id }) { index, widget ->
                if (index > 0) {
                    Separator()
                }
                WidgetRow(
                    widget = widget,
                    manage = manage,
                    onToggle = { enabled -> onToggle(widget, enabled) },
                    onDelete = { onDelete(widget) },
                    onRename = { onRename(widget) },
                    onClone = { onClone(widget) },
                    onEditCode = { onEditCode(widget) },
                    onVersions = { onVersions(widget) },
                )
            }
        }
    }
}

// One overlay card: its name + state on the left, the write controls on the right, and below them the
// browser-source URL in a copyable chip (the page's core value — paste into OBS). Every widget is now
// code-backed (compile-on-save), so Edit code + Versions are available for all of them — gated by the page's
// Editor manage floor. The header row uses the hardened layout: the text block takes weight(1f) and ellipsizes,
// the trailing controls stay single-line.
@Composable
private fun WidgetRow(
    widget: WidgetSummary,
    manage: ManageDecision,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
    onRename: () -> Unit,
    onClone: () -> Unit,
    onEditCode: () -> Unit,
    onVersions: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val stateLabel: String =
        stringResource(
            if (widget.isEnabled) Res.string.widgets_badge_enabled
            else Res.string.widgets_badge_disabled
        )
    val toggleLabel: String = stringResource(Res.string.widgets_toggle_action, widget.name)
    val deleteLabel: String = stringResource(Res.string.widgets_delete_action, widget.name)
    val renameLabel: String = stringResource(Res.string.widgets_rename_action, widget.name)
    val cloneLabel: String = stringResource(Res.string.widgets_clone_action, widget.name)
    val editCodeLabel: String = stringResource(Res.string.widgets_edit_code_action, widget.name)
    val versionsLabel: String = stringResource(Res.string.widgets_versions_action, widget.name)
    val urlLabel: String = stringResource(Res.string.widgets_url_label)

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(
                modifier = Modifier
                    .weight(1f)
                    // One node for the text block: "Alerts, vanilla, enabled.".
                    .clearAndSetSemantics { contentDescription = "${widget.name}, ${widget.framework}, $stateLabel." },
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Text(
                    text = widget.name,
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = "$stateLabel · ${widget.framework}",
                    style = typography.sm,
                    color = if (widget.isEnabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }

            ManageGate(decision = manage) { enabled ->
                Switch(
                    checked = widget.isEnabled,
                    onCheckedChange = onToggle,
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = toggleLabel },
                )
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(
                    onClick = onEditCode,
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = editCodeLabel },
                ) {
                    Text(
                        text = stringResource(Res.string.widgets_edit_code_action_short),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
            // Version history + rollback is a read to open, so it stays enabled below the manage floor; the
            // rollback control inside the dialog carries the write gate.
            TextButton(
                onClick = onVersions,
                modifier = Modifier.semantics { contentDescription = versionsLabel },
            ) {
                Text(
                    text = stringResource(Res.string.widgets_versions_action_short),
                    color = tokens.primary,
                    maxLines = 1,
                )
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(
                    onClick = onRename,
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = renameLabel },
                ) {
                    Text(
                        text = stringResource(Res.string.widgets_rename_action_short),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(
                    onClick = onClone,
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = cloneLabel },
                ) {
                    Text(
                        text = stringResource(Res.string.widgets_clone_action_short),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
            ManageGate(decision = manage) { enabled ->
                GlyphButton(
                    imageVector = TrashGlyph,
                    label = deleteLabel,
                    onClick = onDelete,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
        }

        // The browser-source URL — the operator pastes this into an OBS browser source. Shown labelled in a
        // copyable chip. Missing only if the backend has no overlay token yet for the channel.
        Text(text = urlLabel, style = typography.xs, color = tokens.mutedForeground, maxLines = 1)
        val overlayUrl: String? = widget.overlayUrl?.takeIf { it.isNotBlank() }
        if (overlayUrl != null) {
            CopyValue(
                value = overlayUrl,
                copyLabel = stringResource(Res.string.widgets_url_copy),
                copiedLabel = stringResource(Res.string.widgets_url_copied),
            )
        } else {
            Text(
                text = stringResource(Res.string.widgets_url_missing),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.widgets_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.widgets_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

// The delete-confirm target: the widget's id (the backend address) plus its name (for the confirm message).
private data class PendingDelete(val id: String, val name: String)

// The rollback-confirm target: which widget, and the version to re-serve.
private data class PendingRollback(val widgetId: String, val version: WidgetVersionSummary)

// The framework set the backend accepts for a new widget (CreateWidgetRequest.framework).
private val WIDGET_FRAMEWORKS: List<String> = listOf("vanilla", "vue", "react", "svelte")

// Dialog to create a new widget: pick a framework and, optionally, a starter template. Choosing a template
// adopts its framework and seeds the editor with its source; "Blank" starts from an empty editor on the
// currently-selected framework. On confirm the widget is created and the compile-on-save editor opens.
@Composable
private fun CreateWidgetDialog(
    loadTemplates: suspend () -> ApiResult<List<WidgetTemplate>>,
    onConfirm: (name: String, framework: String, seedSource: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf("") }
    var nameError: Boolean by remember { mutableStateOf(false) }
    var selectedFramework: String by remember { mutableStateOf(WIDGET_FRAMEWORKS.first()) }
    var selectedTemplateKey: String? by remember { mutableStateOf(null) }
    var templatesResult: ApiResult<List<WidgetTemplate>>? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { templatesResult = loadTemplates() }

    val templates: List<WidgetTemplate> =
        (templatesResult as? ApiResult.Ok)?.value ?: emptyList()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.widgets_create_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.widgets_create_name),
                    isError = nameError,
                    errorText = if (nameError) stringResource(Res.string.widgets_create_name_required) else null,
                )

                Text(
                    text = stringResource(Res.string.widgets_create_framework),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                BadgeRow(
                    options = WIDGET_FRAMEWORKS,
                    label = { it },
                    isSelected = { it == selectedFramework },
                    onSelect = { framework ->
                        selectedFramework = framework
                        // Picking a framework directly clears any template — the editor opens blank.
                        selectedTemplateKey = null
                    },
                )

                Text(
                    text = stringResource(Res.string.widgets_create_template),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                if (templatesResult == null) {
                    Text(
                        text = stringResource(Res.string.widgets_create_template_loading),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
                val blankLabel: String = stringResource(Res.string.widgets_create_template_blank)
                BadgeRow(
                    options = listOf<WidgetTemplate?>(null) + templates,
                    label = { it?.name ?: blankLabel },
                    isSelected = { it?.key == selectedTemplateKey },
                    onSelect = { template ->
                        selectedTemplateKey = template?.key
                        if (template != null) selectedFramework = template.framework
                    },
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (name.isBlank()) { nameError = true; return@Button }
                    val seedSource: String =
                        templates.firstOrNull { it.key == selectedTemplateKey }?.source.orEmpty()
                    onConfirm(name.trim(), selectedFramework, seedSource)
                },
            ) {
                Text(stringResource(Res.string.widgets_create_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.widgets_create_dismiss)) }
        },
    )
}

// A wrapped row of selectable badges (3 per line) over a homogeneous option list — the framework picker and the
// template picker share it.
@Composable
private fun <T> BadgeRow(
    options: List<T>,
    label: (T) -> String,
    isSelected: (T) -> Boolean,
    onSelect: (T) -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        options.chunked(3).forEach { rowItems ->
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                rowItems.forEach { option ->
                    Badge(
                        selected = isSelected(option),
                        onClick = { onSelect(option) },
                    ) { Text(label(option), style = typography.xs) }
                }
            }
        }
    }
}

// Dialog to rename an existing widget. Pre-filled with the current name; the operator edits it and confirms.
@Composable
private fun RenameWidgetDialog(
    currentName: String,
    onConfirm: (newName: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf(currentName) }
    var nameError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.widgets_rename_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            AppTextField(
                value = name,
                onValueChange = { name = it; nameError = false },
                label = stringResource(Res.string.widgets_rename_name),
                isError = nameError,
                errorText = if (nameError) stringResource(Res.string.widgets_create_name_required) else null,
            )
        },
        confirmButton = {
            Button(
                onClick = {
                    if (name.isBlank()) { nameError = true; return@Button }
                    onConfirm(name.trim())
                },
            ) {
                Text(stringResource(Res.string.widgets_rename_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.widgets_rename_dismiss)) }
        },
    )
}

// Dialog listing a widget's version history (newest first) with a per-version roll-back control. Fetches its own
// list on open (loading / error / empty / list); rollback is gated at the page's Editor manage floor and hidden
// on the currently-active version (there is nothing to roll back to).
@Composable
private fun WidgetVersionsDialog(
    widget: WidgetSummary,
    manage: ManageDecision,
    loadVersions: suspend () -> ApiResult<List<WidgetVersionSummary>>,
    onRollback: (WidgetVersionSummary) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var result: ApiResult<List<WidgetVersionSummary>>? by remember { mutableStateOf(null) }
    LaunchedEffect(widget.id) { result = loadVersions() }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.widgets_versions_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth().verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                when (val current = result) {
                    null ->
                        Text(
                            text = stringResource(Res.string.widgets_versions_loading),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                    is ApiResult.Failure ->
                        Text(
                            text = stringResource(Res.string.widgets_versions_error, current.error.message),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                    is ApiResult.Ok ->
                        if (current.value.isEmpty()) {
                            Text(
                                text = stringResource(Res.string.widgets_versions_empty),
                                style = typography.sm,
                                color = tokens.mutedForeground,
                            )
                        } else {
                            current.value.forEachIndexed { index, version ->
                                if (index > 0) Separator()
                                WidgetVersionRow(
                                    version = version,
                                    isActive = version.id == widget.activeVersionId,
                                    manage = manage,
                                    onRollback = { onRollback(version) },
                                )
                            }
                        }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.widgets_versions_close)) }
        },
    )
}

// One version row: "Version N", its build status (colored), an "Active" marker on the served version, and a
// roll-back control for every other version (write-gated).
@Composable
private fun WidgetVersionRow(
    version: WidgetVersionSummary,
    isActive: Boolean,
    manage: ManageDecision,
    onRollback: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s1),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(
                text = stringResource(Res.string.widgets_versions_row, version.versionNumber.toString()),
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = version.buildStatus,
                style = typography.xs,
                color = when (version.buildStatus.lowercase()) {
                    "success" -> tokens.primary
                    "error" -> tokens.destructive
                    else -> tokens.mutedForeground
                },
                maxLines = 1,
            )
        }
        if (isActive) {
            Text(
                text = stringResource(Res.string.widgets_versions_active),
                style = typography.xs,
                color = tokens.primary,
                maxLines = 1,
            )
        } else {
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onRollback, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.widgets_rollback_action),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
        }
    }
}

// The gallery browse surface (widgets-overlays.md §5c), reachable from the Overlays header. Lists the public,
// verified widget catalogue with an optional framework filter; each item can be installed into the channel or
// cloned into an editable copy. Fetches its own list on open + on every filter change (loading / error / empty /
// list). Install / Clone are Editor-gated (the page's manage floor); browsing itself is a public read.
@Composable
private fun GalleryBrowseDialog(
    manage: ManageDecision,
    loadGallery: suspend (GalleryListRequest) -> ApiResult<List<GalleryItemSummary>>,
    onInstall: (GalleryItemSummary) -> Unit,
    onClone: (GalleryItemSummary) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var selectedFramework: String? by remember { mutableStateOf(null) }
    var result: ApiResult<List<GalleryItemSummary>>? by remember { mutableStateOf(null) }

    // First open (framework null) and every filter change re-list the catalogue.
    LaunchedEffect(selectedFramework) { result = loadGallery(GalleryListRequest(framework = selectedFramework)) }

    val allLabel: String = stringResource(Res.string.widgets_gallery_filter_all)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.widgets_gallery_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth().verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                // The framework filter — "All" plus each supported framework; picking one re-lists the catalogue.
                BadgeRow(
                    options = listOf<String?>(null) + WIDGET_FRAMEWORKS,
                    label = { it ?: allLabel },
                    isSelected = { it == selectedFramework },
                    onSelect = { selectedFramework = it },
                )

                when (val current: ApiResult<List<GalleryItemSummary>>? = result) {
                    null ->
                        Text(
                            text = stringResource(Res.string.widgets_gallery_loading),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                    is ApiResult.Failure ->
                        Text(
                            text = stringResource(Res.string.widgets_gallery_error, current.error.message),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                    is ApiResult.Ok ->
                        if (current.value.isEmpty()) {
                            Text(
                                text = stringResource(Res.string.widgets_gallery_empty),
                                style = typography.sm,
                                color = tokens.mutedForeground,
                            )
                        } else {
                            current.value.forEach { item ->
                                GalleryItemCard(
                                    item = item,
                                    manage = manage,
                                    onInstall = { onInstall(item) },
                                    onClone = { onClone(item) },
                                )
                            }
                        }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.widgets_gallery_close)) }
        },
    )
}

// One gallery catalogue entry: its name + description, a badge row (framework · trust tier · install count), and
// the two write actions (Install / Clone to edit), each gated at the page's Editor manage floor. Install adds the
// widget to the channel and goes live; Clone forks it into an editable custom copy and opens the code editor.
@Composable
private fun GalleryItemCard(
    item: GalleryItemSummary,
    manage: ManageDecision,
    onInstall: () -> Unit,
    onClone: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val installLabel: String = stringResource(Res.string.widgets_gallery_install_action, item.name)
    val cloneLabel: String = stringResource(Res.string.widgets_gallery_clone_action, item.name)

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = item.name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            item.description?.takeIf { it.isNotBlank() }?.let { description ->
                Text(
                    text = description,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }

            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Badge(variant = BadgeVariant.Outline) { Text(item.framework, style = typography.xs) }
                TrustTierBadge(item.trustTier)
                Text(
                    text = stringResource(Res.string.widgets_gallery_install_count, item.installCount.toString()),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                )
            }

            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ManageGate(decision = manage) { enabled ->
                    Button(
                        onClick = onInstall,
                        enabled = enabled,
                        modifier = Modifier.semantics { contentDescription = installLabel },
                    ) {
                        Text(stringResource(Res.string.widgets_gallery_install_action_short))
                    }
                }
                ManageGate(decision = manage) { enabled ->
                    Button(
                        onClick = onClone,
                        variant = ButtonVariant.Outline,
                        enabled = enabled,
                        modifier = Modifier.semantics { contentDescription = cloneLabel },
                    ) {
                        Text(stringResource(Res.string.widgets_gallery_clone_action_short))
                    }
                }
            }
        }
    }
}

// The trust-tier chip: a localized label on a variant that reads the tier's weight — a first-party widget is the
// loud Default fill, verified-community is Secondary, and an unverified / unknown tier is a quiet Outline.
@Composable
private fun TrustTierBadge(trustTier: String) {
    val typography = LocalTypography.current

    val variant: BadgeVariant =
        when (trustTier) {
            "first_party" -> BadgeVariant.Default
            "verified_community" -> BadgeVariant.Secondary
            else -> BadgeVariant.Outline
        }
    val label: String =
        when (trustTier) {
            "first_party" -> stringResource(Res.string.widgets_gallery_trust_first_party)
            "verified_community" -> stringResource(Res.string.widgets_gallery_trust_verified)
            else -> stringResource(Res.string.widgets_gallery_trust_unverified)
        }

    Badge(variant = variant) { Text(label, style = typography.xs) }
}
