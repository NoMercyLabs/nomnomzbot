// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.bundles.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.RevealableSecretField
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography
import bot.nomnomz.dashboard.core.network.BundleInspection
import bot.nomnomz.dashboard.core.network.ExportItemRef
import bot.nomnomz.dashboard.core.network.ImportPolicy
import bot.nomnomz.dashboard.core.network.InstalledBundle
import bot.nomnomz.dashboard.core.network.MarketplaceItem
import bot.nomnomz.dashboard.feature.bundles.state.BundlesController
import bot.nomnomz.dashboard.feature.bundles.state.BundlesUiState
import bot.nomnomz.dashboard.feature.bundles.state.Exportables
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.bundles_action_error
import nomnomzbot.composeapp.generated.resources.bundles_cancel
import nomnomzbot.composeapp.generated.resources.bundles_description_label
import nomnomzbot.composeapp.generated.resources.bundles_error
import nomnomzbot.composeapp.generated.resources.bundles_export_button
import nomnomzbot.composeapp.generated.resources.bundles_export_hint
import nomnomzbot.composeapp.generated.resources.bundles_export_nothing
import nomnomzbot.composeapp.generated.resources.bundles_group_commands
import nomnomzbot.composeapp.generated.resources.bundles_group_pipelines
import nomnomzbot.composeapp.generated.resources.bundles_group_sounds
import nomnomzbot.composeapp.generated.resources.bundles_group_widgets
import nomnomzbot.composeapp.generated.resources.bundles_import_author
import nomnomzbot.composeapp.generated.resources.bundles_import_button
import nomnomzbot.composeapp.generated.resources.bundles_import_capabilities_label
import nomnomzbot.composeapp.generated.resources.bundles_import_choose
import nomnomzbot.composeapp.generated.resources.bundles_import_items_label
import nomnomzbot.composeapp.generated.resources.bundles_import_issues_label
import nomnomzbot.composeapp.generated.resources.bundles_import_note
import nomnomzbot.composeapp.generated.resources.bundles_import_policy_label
import nomnomzbot.composeapp.generated.resources.bundles_import_version
import nomnomzbot.composeapp.generated.resources.bundles_install_button
import nomnomzbot.composeapp.generated.resources.bundles_installed_at
import nomnomzbot.composeapp.generated.resources.bundles_installed_empty
import nomnomzbot.composeapp.generated.resources.bundles_installed_version
import nomnomzbot.composeapp.generated.resources.bundles_loading
import nomnomzbot.composeapp.generated.resources.bundles_marketplace_browse
import nomnomzbot.composeapp.generated.resources.bundles_marketplace_by
import nomnomzbot.composeapp.generated.resources.bundles_marketplace_empty
import nomnomzbot.composeapp.generated.resources.bundles_marketplace_installs
import nomnomzbot.composeapp.generated.resources.bundles_marketplace_rating
import nomnomzbot.composeapp.generated.resources.bundles_marketplace_search_label
import nomnomzbot.composeapp.generated.resources.bundles_marketplace_type_label
import nomnomzbot.composeapp.generated.resources.bundles_marketplace_unavailable
import nomnomzbot.composeapp.generated.resources.bundles_name_label
import nomnomzbot.composeapp.generated.resources.bundles_policy_overwrite
import nomnomzbot.composeapp.generated.resources.bundles_policy_rename
import nomnomzbot.composeapp.generated.resources.bundles_policy_skip
import nomnomzbot.composeapp.generated.resources.bundles_publish_dialog_title
import nomnomzbot.composeapp.generated.resources.bundles_publish_name_label
import nomnomzbot.composeapp.generated.resources.bundles_publish_note
import nomnomzbot.composeapp.generated.resources.bundles_publish_open
import nomnomzbot.composeapp.generated.resources.bundles_publish_submit
import nomnomzbot.composeapp.generated.resources.bundles_publish_summary_label
import nomnomzbot.composeapp.generated.resources.bundles_publish_tags_label
import nomnomzbot.composeapp.generated.resources.bundles_publish_title
import nomnomzbot.composeapp.generated.resources.bundles_publish_token_desc
import nomnomzbot.composeapp.generated.resources.bundles_publish_token_label
import nomnomzbot.composeapp.generated.resources.bundles_publish_token_remove
import nomnomzbot.composeapp.generated.resources.bundles_publish_token_save
import nomnomzbot.composeapp.generated.resources.bundles_publish_token_stored
import nomnomzbot.composeapp.generated.resources.bundles_publish_version_label
import nomnomzbot.composeapp.generated.resources.bundles_retry
import nomnomzbot.composeapp.generated.resources.bundles_source_local
import nomnomzbot.composeapp.generated.resources.bundles_source_marketplace
import nomnomzbot.composeapp.generated.resources.bundles_subtitle
import nomnomzbot.composeapp.generated.resources.bundles_tab_export
import nomnomzbot.composeapp.generated.resources.bundles_tab_import
import nomnomzbot.composeapp.generated.resources.bundles_tab_installed
import nomnomzbot.composeapp.generated.resources.bundles_tab_marketplace
import nomnomzbot.composeapp.generated.resources.bundles_title
import nomnomzbot.composeapp.generated.resources.bundles_uninstall
import nomnomzbot.composeapp.generated.resources.bundles_uninstall_confirm
import nomnomzbot.composeapp.generated.resources.bundles_uninstall_message
import nomnomzbot.composeapp.generated.resources.bundles_uninstall_title
import nomnomzbot.composeapp.generated.resources.bundles_version_label
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Bundles page (frontend-ia.md, Connect group; bundles.md §5–§6): portable content packs. Four tabs off one
// [BundlesController] — Export (pick the channel's own content → a portable ZIP), Import (pick a ZIP, inspect it
// first, then install under a conflict policy), Installed (what's on the channel, uninstallable), and Marketplace
// (the OPTIONAL hosted catalogue, with a Broadcaster-gated publish surface). Export/import gate at the page's
// Editor manage floor; publish + the publisher token gate at Broadcaster. The hosted marketplace can be
// unavailable — the screen renders an honest empty-state card rather than pretending it failed.
@Composable
fun BundlesScreen(controller: BundlesController, role: ManagementRole?) {
    val state: BundlesUiState by controller.state.collectAsStateWithLifecycle()
    val scope: CoroutineScope = rememberCoroutineScope()
    val spacing: Spacing = LocalSpacing.current

    // Export/import are the page's own Editor floor; publish + publisher token are Broadcaster-only.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Bundles)
    val managePublish: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Broadcaster)

    var tab: Int by remember { mutableStateOf(0) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: BundlesUiState = state) {
            is BundlesUiState.Loading -> CenteredMessage(stringResource(Res.string.bundles_loading))
            is BundlesUiState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is BundlesUiState.Ready ->
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    PageHeader(
                        title = stringResource(Res.string.bundles_title),
                        subtitle = stringResource(Res.string.bundles_subtitle),
                    )
                    current.actionError?.let {
                        ActionErrorBanner(message = stringResource(Res.string.bundles_action_error, it))
                    }
                    current.notice?.let { NoticeText(it) }

                    TabsList {
                        TabsTrigger(selected = tab == 0, onClick = { tab = 0 }) {
                            Text(text = stringResource(Res.string.bundles_tab_export))
                        }
                        TabsTrigger(selected = tab == 1, onClick = { tab = 1 }) {
                            Text(text = stringResource(Res.string.bundles_tab_import))
                        }
                        TabsTrigger(selected = tab == 2, onClick = { tab = 2 }) {
                            Text(text = stringResource(Res.string.bundles_tab_installed))
                        }
                        TabsTrigger(selected = tab == 3, onClick = { tab = 3 }) {
                            Text(text = stringResource(Res.string.bundles_tab_marketplace))
                        }
                    }

                    when (tab) {
                        0 ->
                            ExportTab(
                                exportables = current.exportables,
                                manage = manage,
                                onExport = { items, name, version, description ->
                                    scope.launch { controller.exportBundle(items, name, version, description) }
                                },
                            )
                        1 ->
                            ImportTab(
                                inspection = current.inspection,
                                manage = manage,
                                onPick = { scope.launch { controller.pickAndInspect() } },
                                onImport = { policy -> scope.launch { controller.importInspected(policy) } },
                                onCancel = { controller.clearInspection() },
                            )
                        2 ->
                            InstalledTab(
                                installed = current.installed,
                                manage = manage,
                                onUninstall = { id -> scope.launch { controller.uninstall(id) } },
                            )
                        else ->
                            MarketplaceTab(
                                available = current.marketplaceAvailable,
                                items = current.marketplace,
                                hasPublisherToken = current.hasPublisherToken,
                                manageInstall = manage,
                                managePublish = managePublish,
                                onBrowse = { q, type -> scope.launch { controller.browseMarketplace(q, type) } },
                                onInstall = { itemId, policy ->
                                    scope.launch { controller.installFromMarketplace(itemId, policy) }
                                },
                                onSetToken = { token -> scope.launch { controller.setPublisherToken(token) } },
                                onClearToken = { scope.launch { controller.clearPublisherToken() } },
                                onPublish = { name, version, summary, tags ->
                                    scope.launch { controller.publish(name, version, summary, tags) }
                                },
                            )
                    }
                }
        }
    }
}

// ── Export tab ───────────────────────────────────────────────────────────────

@Composable
private fun ExportTab(
    exportables: Exportables,
    manage: ManageDecision,
    onExport: (items: List<ExportItemRef>, name: String, version: String, description: String?) -> Unit,
) {
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current

    val selected: MutableState<Set<ExportItemRef>> = remember { mutableStateOf(emptySet<ExportItemRef>()) }
    var name: String by remember { mutableStateOf("") }
    var version: String by remember { mutableStateOf("1.0.0") }
    var description: String by remember { mutableStateOf("") }

    val nothingToExport: Boolean =
        exportables.commands.isEmpty() &&
            exportables.pipelines.isEmpty() &&
            exportables.widgets.isEmpty() &&
            exportables.sounds.isEmpty()

    val canExport: Boolean = name.isNotBlank() && selected.value.isNotEmpty()

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            AppTextField(
                value = name,
                onValueChange = { name = it },
                label = stringResource(Res.string.bundles_name_label),
                modifier = Modifier.fillMaxWidth(),
            )
            AppTextField(
                value = version,
                onValueChange = { version = it },
                label = stringResource(Res.string.bundles_version_label),
                modifier = Modifier.fillMaxWidth(),
            )
            Textarea(
                value = description,
                onValueChange = { description = it },
                label = stringResource(Res.string.bundles_description_label),
                modifier = Modifier.fillMaxWidth(),
                minLines = 2,
            )
            Text(
                text = stringResource(Res.string.bundles_export_hint),
                style = typography.xs,
                color = tokens.mutedForeground,
            )

            if (nothingToExport) {
                Text(
                    text = stringResource(Res.string.bundles_export_nothing),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            } else {
                ExportGroup(stringResource(Res.string.bundles_group_commands), "command", exportables.commands, selected)
                ExportGroup(stringResource(Res.string.bundles_group_pipelines), "pipeline", exportables.pipelines, selected)
                ExportGroup(stringResource(Res.string.bundles_group_widgets), "widget", exportables.widgets, selected)
                ExportGroup(stringResource(Res.string.bundles_group_sounds), "sound", exportables.sounds, selected)
            }

            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = {
                        onExport(
                            selected.value.toList(),
                            name.trim(),
                            version.trim(),
                            description.trim().ifBlank { null },
                        )
                    },
                    enabled = enabled && canExport,
                ) {
                    Text(text = stringResource(Res.string.bundles_export_button))
                }
            }
        }
    }
}

@Composable
private fun ExportGroup(
    title: String,
    type: String,
    items: List<Pair<String, String>>,
    selected: MutableState<Set<ExportItemRef>>,
) {
    if (items.isEmpty()) return
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = title, style = typography.sm, color = tokens.mutedForeground)
        items.forEach { (id, label) ->
            val ref: ExportItemRef = ExportItemRef(type = type, id = id)
            val isSelected: Boolean = ref in selected.value
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(
                    text = label,
                    style = typography.sm,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
                Switch(
                    checked = isSelected,
                    onCheckedChange = { on ->
                        selected.value = if (on) selected.value + ref else selected.value - ref
                    },
                )
            }
        }
    }
}

// ── Import tab ───────────────────────────────────────────────────────────────

@Composable
private fun ImportTab(
    inspection: BundleInspection?,
    manage: ManageDecision,
    onPick: () -> Unit,
    onImport: (policy: String) -> Unit,
    onCancel: () -> Unit,
) {
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current

    var policy: String by remember { mutableStateOf(ImportPolicy.Rename) }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        ManageGate(decision = manage) { enabled ->
            Button(onClick = onPick, enabled = enabled) {
                Text(text = stringResource(Res.string.bundles_import_choose))
            }
        }
        Text(
            text = stringResource(Res.string.bundles_import_note),
            style = typography.xs,
            color = tokens.mutedForeground,
        )

        if (inspection != null) {
            val hasIssues: Boolean = inspection.issues.isNotEmpty()
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(
                    modifier = Modifier.padding(spacing.s4),
                    verticalArrangement = Arrangement.spacedBy(spacing.s3),
                ) {
                    Text(
                        text = inspection.manifest.metadata.name,
                        style = typography.base,
                        color = tokens.cardForeground,
                    )
                    Text(
                        text = stringResource(Res.string.bundles_import_version, inspection.manifest.metadata.version),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                    inspection.manifest.metadata.author?.let { author ->
                        Text(
                            text = stringResource(Res.string.bundles_import_author, author),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                    }

                    if (inspection.manifest.items.isNotEmpty()) {
                        Text(
                            text = stringResource(Res.string.bundles_import_items_label),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                        inspection.manifest.items.forEach { item ->
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                            ) {
                                Badge(variant = BadgeVariant.Outline) { Text(text = item.type, style = typography.xs) }
                                Text(
                                    text = item.name,
                                    style = typography.sm,
                                    color = tokens.cardForeground,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                        }
                    }

                    if (inspection.capabilities.isNotEmpty()) {
                        Text(
                            text = stringResource(Res.string.bundles_import_capabilities_label),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                        FlowRow(
                            horizontalArrangement = Arrangement.spacedBy(spacing.s1),
                            verticalArrangement = Arrangement.spacedBy(spacing.s1),
                        ) {
                            inspection.capabilities.forEach { capability ->
                                Badge(variant = BadgeVariant.Secondary) { Text(text = capability, style = typography.xs) }
                            }
                        }
                    }

                    if (hasIssues) {
                        Text(
                            text = stringResource(Res.string.bundles_import_issues_label),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                        inspection.issues.forEach { issue ->
                            Text(text = issue, style = typography.xs, color = tokens.destructive)
                        }
                    }

                    Text(
                        text = stringResource(Res.string.bundles_import_policy_label),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        ImportPolicy.all.forEach { option ->
                            ToggleChip(
                                label = stringResource(policyLabel(option)),
                                selected = option == policy,
                                onClick = { policy = option },
                            )
                        }
                    }

                    Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        ManageGate(decision = manage) { enabled ->
                            Button(onClick = { onImport(policy) }, enabled = enabled && !hasIssues) {
                                Text(text = stringResource(Res.string.bundles_import_button))
                            }
                        }
                        TextButton(onClick = onCancel) {
                            Text(text = stringResource(Res.string.bundles_cancel), color = tokens.mutedForeground)
                        }
                    }
                }
            }
        }
    }
}

// ── Installed tab ────────────────────────────────────────────────────────────

@Composable
private fun InstalledTab(
    installed: List<InstalledBundle>,
    manage: ManageDecision,
    onUninstall: (id: String) -> Unit,
) {
    if (installed.isEmpty()) {
        CenteredMessage(stringResource(Res.string.bundles_installed_empty))
        return
    }
    var pendingUninstall: InstalledBundle? by remember { mutableStateOf(null) }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column {
            installed.forEachIndexed { index, bundle ->
                InstalledRow(bundle = bundle, manage = manage, onUninstall = { pendingUninstall = bundle })
                if (index < installed.lastIndex) Separator()
            }
        }
    }

    pendingUninstall?.let { bundle ->
        ConfirmDialog(
            title = stringResource(Res.string.bundles_uninstall_title),
            message = stringResource(Res.string.bundles_uninstall_message, bundle.name),
            confirmLabel = stringResource(Res.string.bundles_uninstall_confirm),
            dismissLabel = stringResource(Res.string.bundles_cancel),
            destructive = true,
            onConfirm = {
                pendingUninstall = null
                onUninstall(bundle.id)
            },
            onDismiss = { pendingUninstall = null },
        )
    }
}

@Composable
private fun InstalledRow(bundle: InstalledBundle, manage: ManageDecision, onUninstall: () -> Unit) {
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current

    val fromMarketplace: Boolean = bundle.source == "marketplace"

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                Text(
                    text = bundle.name,
                    style = typography.base,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Badge(variant = if (fromMarketplace) BadgeVariant.Default else BadgeVariant.Secondary) {
                    Text(
                        text =
                            if (fromMarketplace) stringResource(Res.string.bundles_source_marketplace)
                            else stringResource(Res.string.bundles_source_local),
                        style = typography.xs,
                    )
                }
            }
            Text(
                text = stringResource(Res.string.bundles_installed_version, bundle.version),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
            Text(
                text = stringResource(Res.string.bundles_installed_at, bundle.installedAt),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }

        ManageGate(decision = manage) { enabled ->
            Button(onClick = onUninstall, variant = ButtonVariant.Destructive, enabled = enabled) {
                Text(text = stringResource(Res.string.bundles_uninstall))
            }
        }
    }
}

// ── Marketplace tab ──────────────────────────────────────────────────────────

@Composable
private fun MarketplaceTab(
    available: Boolean,
    items: List<MarketplaceItem>,
    hasPublisherToken: Boolean,
    manageInstall: ManageDecision,
    managePublish: ManageDecision,
    onBrowse: (q: String?, type: String?) -> Unit,
    onInstall: (itemId: String, policy: String) -> Unit,
    onSetToken: (token: String) -> Unit,
    onClearToken: () -> Unit,
    onPublish: (name: String, version: String, summary: String, tagsCsv: String) -> Unit,
) {
    val spacing: Spacing = LocalSpacing.current

    var query: String by remember { mutableStateOf("") }
    var type: String by remember { mutableStateOf("") }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.Bottom,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            AppTextField(
                value = query,
                onValueChange = { query = it },
                label = stringResource(Res.string.bundles_marketplace_search_label),
                modifier = Modifier.weight(1f),
            )
            AppTextField(
                value = type,
                onValueChange = { type = it },
                label = stringResource(Res.string.bundles_marketplace_type_label),
                modifier = Modifier.weight(1f),
            )
            Button(onClick = { onBrowse(query.trim().ifBlank { null }, type.trim().ifBlank { null }) }) {
                Text(text = stringResource(Res.string.bundles_marketplace_browse))
            }
        }

        if (!available) {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.fillMaxWidth().padding(spacing.s6)) {
                    CenteredMessage(stringResource(Res.string.bundles_marketplace_unavailable))
                }
            }
        } else if (items.isEmpty()) {
            CenteredMessage(stringResource(Res.string.bundles_marketplace_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    items.forEachIndexed { index, item ->
                        MarketplaceRow(item = item, manage = manageInstall, onInstall = onInstall)
                        if (index < items.lastIndex) Separator()
                    }
                }
            }
        }

        PublishCard(
            hasPublisherToken = hasPublisherToken,
            manage = managePublish,
            onSetToken = onSetToken,
            onClearToken = onClearToken,
            onPublish = onPublish,
        )
    }
}

@Composable
private fun MarketplaceRow(
    item: MarketplaceItem,
    manage: ManageDecision,
    onInstall: (itemId: String, policy: String) -> Unit,
) {
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current

    var policy: String by remember { mutableStateOf(ImportPolicy.Rename) }

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = item.name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = stringResource(Res.string.bundles_marketplace_rating, item.rating.toString()),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                Text(
                    text = stringResource(Res.string.bundles_marketplace_installs, item.installs.toString()),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }
        Text(
            text = stringResource(Res.string.bundles_marketplace_by, item.author),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        if (item.summary.isNotBlank()) {
            Text(text = item.summary, style = typography.sm, color = tokens.cardForeground)
        }
        if (item.capabilities.isNotEmpty()) {
            FlowRow(
                horizontalArrangement = Arrangement.spacedBy(spacing.s1),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                item.capabilities.forEach { capability ->
                    Badge(variant = BadgeVariant.Secondary) { Text(text = capability, style = typography.xs) }
                }
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ImportPolicy.all.forEach { option ->
                    ToggleChip(
                        label = stringResource(policyLabel(option)),
                        selected = option == policy,
                        onClick = { policy = option },
                    )
                }
            }
            ManageGate(decision = manage) { enabled ->
                Button(onClick = { onInstall(item.itemId, policy) }, enabled = enabled) {
                    Text(text = stringResource(Res.string.bundles_install_button))
                }
            }
        }
    }
}

@Composable
private fun PublishCard(
    hasPublisherToken: Boolean,
    manage: ManageDecision,
    onSetToken: (token: String) -> Unit,
    onClearToken: () -> Unit,
    onPublish: (name: String, version: String, summary: String, tagsCsv: String) -> Unit,
) {
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current

    var token: String by remember { mutableStateOf("") }
    var showPublish: Boolean by remember { mutableStateOf(false) }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = stringResource(Res.string.bundles_publish_title),
                style = typography.base,
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(Res.string.bundles_publish_token_desc),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            ManageGate(decision = manage) { enabled ->
                RevealableSecretField(
                    value = token,
                    onValueChange = { token = it },
                    label = stringResource(Res.string.bundles_publish_token_label),
                    modifier = Modifier.fillMaxWidth(),
                    enabled = enabled,
                    supportingText =
                        if (hasPublisherToken) stringResource(Res.string.bundles_publish_token_stored) else null,
                )
            }
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ManageGate(decision = manage) { enabled ->
                    Button(
                        onClick = {
                            onSetToken(token.trim())
                            token = ""
                        },
                        enabled = enabled && token.isNotBlank(),
                    ) {
                        Text(text = stringResource(Res.string.bundles_publish_token_save))
                    }
                }
                if (hasPublisherToken) {
                    ManageGate(decision = manage) { enabled ->
                        TextButton(onClick = onClearToken, enabled = enabled) {
                            Text(
                                text = stringResource(Res.string.bundles_publish_token_remove),
                                color = if (enabled) tokens.destructive else tokens.mutedForeground,
                            )
                        }
                    }
                }
            }
            ManageGate(decision = manage) { enabled ->
                Button(onClick = { showPublish = true }, variant = ButtonVariant.Outline, enabled = enabled) {
                    Text(text = stringResource(Res.string.bundles_publish_open))
                }
            }
            Text(
                text = stringResource(Res.string.bundles_publish_note),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
    }

    if (showPublish) {
        PublishDialog(
            onDismiss = { showPublish = false },
            onSubmit = { name, version, summary, tags ->
                showPublish = false
                onPublish(name, version, summary, tags)
            },
        )
    }
}

@Composable
private fun PublishDialog(
    onDismiss: () -> Unit,
    onSubmit: (name: String, version: String, summary: String, tagsCsv: String) -> Unit,
) {
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current

    var name: String by remember { mutableStateOf("") }
    var version: String by remember { mutableStateOf("1.0.0") }
    var summary: String by remember { mutableStateOf("") }
    var tags: String by remember { mutableStateOf("") }

    val canSubmit: Boolean = name.isNotBlank() && version.isNotBlank()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.bundles_publish_dialog_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it },
                    label = stringResource(Res.string.bundles_publish_name_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = version,
                    onValueChange = { version = it },
                    label = stringResource(Res.string.bundles_publish_version_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = summary,
                    onValueChange = { summary = it },
                    label = stringResource(Res.string.bundles_publish_summary_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = tags,
                    onValueChange = { tags = it },
                    label = stringResource(Res.string.bundles_publish_tags_label),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onSubmit(name.trim(), version.trim(), summary.trim(), tags.trim()) },
                enabled = canSubmit,
            ) {
                Text(
                    text = stringResource(Res.string.bundles_publish_submit),
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.bundles_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// ── shared bits ──────────────────────────────────────────────────────────────

@Composable
private fun ToggleChip(label: String, selected: Boolean, onClick: () -> Unit) {
    Badge(
        variant = if (selected) BadgeVariant.Default else BadgeVariant.Outline,
        selected = selected,
        onClick = onClick,
    ) {
        Text(text = label, maxLines = 1)
    }
}

private fun policyLabel(policy: String): StringResource =
    when (policy) {
        ImportPolicy.Rename -> Res.string.bundles_policy_rename
        ImportPolicy.Overwrite -> Res.string.bundles_policy_overwrite
        ImportPolicy.Skip -> Res.string.bundles_policy_skip
        else -> Res.string.bundles_policy_rename
    }

@Composable
private fun NoticeText(text: String) {
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current
    Text(text = text, style = typography.sm, color = tokens.mutedForeground, modifier = Modifier.fillMaxWidth())
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.bundles_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.bundles_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
