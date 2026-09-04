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

// Platform content authoring (S-ADMIN-2b, platform-admin.md §2-§5): the definitions list, a
// definition's full version history, drafting a new version, and the publish-preview → confirm flow that
// surfaces §2.1's counted blast radius before a publish can commit. `saas`-only (§0 marker) — this tab is
// reachable only from the Admin plane, which itself only exists where a Plane-C IAM principal exists at all.
//
// Placement: added to the EXISTING 11-tab admin bar as one more tab, positioned directly after Billing —
// grouped with the other "shape what the platform offers" tabs (Flags, Billing) rather than the
// tenant-operations cluster (IAM, Tenants, Audit, Spam Defaults, Providers) that follows it. S-ADMIN-9
// (regroup all 11+1 tabs by job) is its own future slice; this placement is the smallest defensible step
// toward that grouping rather than a blind 12th tab appended at the end.

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.TextStyle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogDescription
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.Tooltip
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.network.PlatformContentDefinition
import bot.nomnomz.dashboard.core.network.PlatformContentPublishModes
import bot.nomnomz.dashboard.core.network.PlatformContentVersion
import bot.nomnomz.dashboard.core.network.PublishPreview
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_content_row_type
import nomnomzbot.composeapp.generated.resources.admin_content_author_denied
import nomnomzbot.composeapp.generated.resources.admin_content_back_to_list
import nomnomzbot.composeapp.generated.resources.admin_content_create
import nomnomzbot.composeapp.generated.resources.admin_content_current_version
import nomnomzbot.composeapp.generated.resources.admin_content_description_label
import nomnomzbot.composeapp.generated.resources.admin_content_draft_new_version
import nomnomzbot.composeapp.generated.resources.admin_content_draft_pending
import nomnomzbot.composeapp.generated.resources.admin_content_empty
import nomnomzbot.composeapp.generated.resources.admin_content_force_denied
import nomnomzbot.composeapp.generated.resources.admin_content_key_label
import nomnomzbot.composeapp.generated.resources.admin_content_kind_command
import nomnomzbot.composeapp.generated.resources.admin_content_name_label
import nomnomzbot.composeapp.generated.resources.admin_content_new
import nomnomzbot.composeapp.generated.resources.admin_content_payload_label
import nomnomzbot.composeapp.generated.resources.admin_content_preview_affected
import nomnomzbot.composeapp.generated.resources.admin_content_preview_loading
import nomnomzbot.composeapp.generated.resources.admin_content_preview_none
import nomnomzbot.composeapp.generated.resources.admin_content_preview_skipped
import nomnomzbot.composeapp.generated.resources.admin_content_publish
import nomnomzbot.composeapp.generated.resources.admin_content_publish_confirm
import nomnomzbot.composeapp.generated.resources.admin_content_publish_denied
import nomnomzbot.composeapp.generated.resources.admin_content_publish_force_justification_label
import nomnomzbot.composeapp.generated.resources.admin_content_publish_force_justification_required
import nomnomzbot.composeapp.generated.resources.admin_content_publish_mode_force
import nomnomzbot.composeapp.generated.resources.admin_content_publish_mode_force_desc
import nomnomzbot.composeapp.generated.resources.admin_content_publish_mode_new
import nomnomzbot.composeapp.generated.resources.admin_content_publish_mode_new_desc
import nomnomzbot.composeapp.generated.resources.admin_content_publish_mode_update
import nomnomzbot.composeapp.generated.resources.admin_content_publish_mode_update_desc
import nomnomzbot.composeapp.generated.resources.admin_content_publish_note_label
import nomnomzbot.composeapp.generated.resources.admin_content_publish_submitting
import nomnomzbot.composeapp.generated.resources.admin_content_publish_success
import nomnomzbot.composeapp.generated.resources.admin_content_publish_title
import nomnomzbot.composeapp.generated.resources.admin_content_read_denied
import nomnomzbot.composeapp.generated.resources.admin_content_retire
import nomnomzbot.composeapp.generated.resources.admin_content_retire_confirm
import nomnomzbot.composeapp.generated.resources.admin_content_retired
import nomnomzbot.composeapp.generated.resources.admin_content_sample_tenants
import nomnomzbot.composeapp.generated.resources.admin_content_saas_marker
import nomnomzbot.composeapp.generated.resources.admin_content_version_draft
import nomnomzbot.composeapp.generated.resources.admin_content_version_label
import nomnomzbot.composeapp.generated.resources.admin_content_version_published
import nomnomzbot.composeapp.generated.resources.admin_content_versions_empty
import nomnomzbot.composeapp.generated.resources.admin_content_versions_title
import org.jetbrains.compose.resources.stringResource

/** The Plane-C `content:*` action keys this tab gates on — resolved from the caller's OWN effective
 * permissions (never assumed) so a principal with read-only access still opens the tab and sees everything,
 * with every write control disabled and explained rather than hidden or silently broken. */
private object ContentPermissions {
    const val Read: String = "content:read"
    const val Author: String = "content:author"
    const val Publish: String = "content:publish"
    const val PublishForce: String = "content:publish:force"
}

/** Resolves the caller's own effective `content:*` keys — the intersection of [AdminState.currentUserId]'s
 * principal (matched by `userId`, mirroring [UserPlatformAccess]'s own lookup) and
 * [AdminState.effectivePermissions]. Null (not empty) until the lookup is loaded, so the tab can tell
 * "still resolving" from "resolved to nothing" and default every gate to denied rather than briefly
 * flashing every control as enabled. */
private fun ownContentKeys(state: AdminState, currentUserId: String?): Set<String>? {
    val principal = state.principals.firstOrNull { it.userId == currentUserId } ?: return emptySet()
    return state.effectivePermissions[principal.id]?.toSet()
}

@Composable
internal fun ContentTab(state: AdminState, controller: AdminController, currentUserId: String?) {
    val scope = rememberCoroutineScope()
    LaunchedEffect(Unit) {
        if (state.contentDefinitions.isEmpty()) controller.loadContentDefinitions()
    }

    val ownKeys: Set<String>? = ownContentKeys(state, currentUserId)
    // Below the read floor: the whole tab is a denial, not a silently empty list — hiding the CONTENT
    // of the tab still leaves the tab itself reachable via the tab bar, so this states plainly why nothing
    // is here rather than looking like a load that quietly failed.
    if (ownKeys != null && ContentPermissions.Read !in ownKeys) {
        ContentReadDenied()
        return
    }

    val selected = state.selectedContentDefinition
    if (selected != null) {
        ContentDefinitionDetail(
            state = state,
            controller = controller,
            ownKeys = ownKeys,
            onBack = { controller.closeContentDefinition() },
        )
    } else {
        ContentDefinitionList(state = state, controller = controller, ownKeys = ownKeys)
    }
}

@Composable
private fun ContentReadDenied() {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    Column(modifier = Modifier.fillMaxSize().padding(spacing.s4), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = stringResource(Res.string.admin_content_read_denied), style = typography.base, color = tokens.mutedForeground)
    }
}

@Composable
private fun ContentDefinitionList(state: AdminState, controller: AdminController, ownKeys: Set<String>?) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    var showCreate: Boolean by remember { mutableStateOf(false) }

    val canAuthor: ManageDecision = manageDecision(
        ownKeys,
        ContentPermissions.Author,
        stringResource(Res.string.admin_content_author_denied),
    )

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.admin_content_saas_marker),
            style = typography.xs,
            color = tokens.mutedForeground,
        )

        state.contentError?.let { ActionErrorBanner(message = it) }
        state.contentActionError?.let { ActionErrorBanner(message = it) }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(text = stringResource(Res.string.admin_content_kind_command), style = typography.base, color = tokens.foreground)
            ManageGate(decision = canAuthor) { enabled ->
                GatedButton(enabled = enabled, decision = canAuthor, onClick = { showCreate = true }) {
                    Text(text = stringResource(Res.string.admin_content_new))
                }
            }
        }

        if (state.contentLoading) {
            Box(modifier = Modifier.fillMaxWidth().padding(spacing.s6), contentAlignment = Alignment.Center) {
                Spinner(color = tokens.primary)
            }
        } else if (state.contentDefinitions.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_content_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.contentDefinitions.forEachIndexed { index, definition ->
                        DefinitionRow(
                            definition = definition,
                            onOpen = { scope.launch { controller.openContentDefinition(definition.id) } },
                        )
                        if (index < state.contentDefinitions.lastIndex) Separator()
                    }
                }
            }
        }
    }

    if (showCreate) {
        CreateDefinitionDialog(
            onDismiss = { showCreate = false },
            onCreate = { key, name, description, payload ->
                showCreate = false
                scope.launch { controller.createContentDefinition(key, name, description, payload) }
            },
        )
    }
}

@Composable
private fun DefinitionRow(definition: PlatformContentDefinition, onOpen: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            val definitionLabel: String =
                resolveRowLabel(
                    primary = definition.displayName,
                    secondary = definition.key,
                    typeLabel = stringResource(Res.string.admin_content_row_type),
                    discriminatorSource = definition.id,
                )
            Text(text = definitionLabel, style = typography.sm, color = tokens.cardForeground)
            Text(text = definition.key, style = typography.xs, color = tokens.mutedForeground)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            if (definition.retiredAt != null) {
                Badge(variant = BadgeVariant.Destructive) {
                    Text(text = stringResource(Res.string.admin_content_retired), style = typography.xs)
                }
            } else if (definition.currentVersion != null) {
                Badge(variant = BadgeVariant.Default) {
                    Text(
                        text = stringResource(Res.string.admin_content_current_version, definition.currentVersion),
                        style = typography.xs,
                    )
                }
            } else {
                Badge(variant = BadgeVariant.Outline) {
                    Text(text = stringResource(Res.string.admin_content_draft_pending), style = typography.xs)
                }
            }
            Button(onClick = onOpen, variant = ButtonVariant.Ghost, size = ButtonSize.Sm) {
                Text(text = stringResource(Res.string.admin_content_versions_title), style = typography.xs)
            }
        }
    }
}

@Composable
private fun CreateDefinitionDialog(
    onDismiss: () -> Unit,
    onCreate: (key: String, displayName: String, description: String?, payloadJson: String) -> Unit,
) {
    var key: String by remember { mutableStateOf("") }
    var displayName: String by remember { mutableStateOf("") }
    var description: String by remember { mutableStateOf("") }
    var payloadJson: String by remember { mutableStateOf("") }

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_content_new))
        AppTextField(
            value = key,
            onValueChange = { key = it },
            label = stringResource(Res.string.admin_content_key_label),
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = displayName,
            onValueChange = { displayName = it },
            label = stringResource(Res.string.admin_content_name_label),
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = description,
            onValueChange = { description = it },
            label = stringResource(Res.string.admin_content_description_label),
            modifier = Modifier.fillMaxWidth(),
        )
        JsonPayloadField(
            value = payloadJson,
            onValueChange = { payloadJson = it },
            label = stringResource(Res.string.admin_content_payload_label),
        )
        DialogFooter {
            Button(onClick = onDismiss, variant = ButtonVariant.Ghost) {
                Text(text = stringResource(Res.string.admin_cancel))
            }
            Button(
                onClick = { onCreate(key.trim(), displayName.trim(), description, payloadJson) },
                enabled = key.isNotBlank() && displayName.isNotBlank() && payloadJson.isNotBlank(),
            ) {
                Text(text = stringResource(Res.string.admin_content_create))
            }
        }
    }
}

@Composable
private fun ContentDefinitionDetail(
    state: AdminState,
    controller: AdminController,
    ownKeys: Set<String>?,
    onBack: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    val detail = state.selectedContentDefinition ?: return

    var showDraftEditor: Boolean by remember { mutableStateOf(false) }
    var showRetireConfirm: Boolean by remember { mutableStateOf(false) }
    var publishTarget: PlatformContentVersion? by remember { mutableStateOf(null) }

    val canAuthor: ManageDecision = manageDecision(ownKeys, ContentPermissions.Author, stringResource(Res.string.admin_content_author_denied))

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Button(onClick = onBack, variant = ButtonVariant.Ghost, size = ButtonSize.Sm) {
            Text(text = stringResource(Res.string.admin_content_back_to_list))
        }

        state.contentDetailError?.let { ActionErrorBanner(message = it) }
        state.contentActionError?.let { ActionErrorBanner(message = it) }
        state.lastPublishJob?.let { job ->
            Text(
                text = stringResource(Res.string.admin_content_publish_success, job.confirmedAffectedCount ?: job.previewAffectedCount),
                style = typography.sm,
                color = tokens.primary,
            )
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column {
                val detailLabel: String =
                    resolveRowLabel(
                        primary = detail.definition.displayName,
                        secondary = detail.definition.key,
                        typeLabel = stringResource(Res.string.admin_content_row_type),
                        discriminatorSource = detail.definition.id,
                    )
                Text(text = detailLabel, style = typography.base, color = tokens.foreground)
                Text(text = detail.definition.key, style = typography.xs, color = tokens.mutedForeground)
            }
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ManageGate(decision = canAuthor) { enabled ->
                    GatedButton(enabled = enabled, decision = canAuthor, onClick = { showDraftEditor = true }) {
                        Text(text = stringResource(Res.string.admin_content_draft_new_version))
                    }
                }
                if (detail.definition.retiredAt == null) {
                    ManageGate(decision = canAuthor) { enabled ->
                        GatedButton(
                            enabled = enabled,
                            decision = canAuthor,
                            variant = ButtonVariant.DestructiveGhost,
                            onClick = { showRetireConfirm = true },
                        ) {
                            Text(text = stringResource(Res.string.admin_content_retire))
                        }
                    }
                }
            }
        }

        if (state.contentDetailLoading) {
            Box(modifier = Modifier.fillMaxWidth().padding(spacing.s6), contentAlignment = Alignment.Center) {
                Spinner(color = tokens.primary)
            }
        }

        Text(text = stringResource(Res.string.admin_content_versions_title), style = typography.base, color = tokens.foreground)
        if (detail.versions.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_content_versions_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    // Newest first — the version an owner is most likely to act on next.
                    val ordered = detail.versions.sortedByDescending { it.version }
                    ordered.forEachIndexed { index, version ->
                        VersionRow(
                            version = version,
                            onPublish = { publishTarget = version },
                            canPublish = ownKeys == null || ContentPermissions.Publish in ownKeys,
                        )
                        if (index < ordered.lastIndex) Separator()
                    }
                }
            }
        }
    }

    if (showDraftEditor) {
        DraftVersionDialog(
            initialPayload = detail.versions.maxByOrNull { it.version }?.payloadJson ?: "",
            onDismiss = { showDraftEditor = false },
            onDraft = { payload ->
                showDraftEditor = false
                scope.launch { controller.draftContentVersion(payload) }
            },
        )
    }

    if (showRetireConfirm) {
        ConfirmDialog(
            title = stringResource(Res.string.admin_content_retire),
            message = stringResource(Res.string.admin_content_retire_confirm, detail.definition.displayName),
            confirmLabel = stringResource(Res.string.admin_content_retire),
            dismissLabel = stringResource(Res.string.admin_cancel),
            destructive = true,
            onConfirm = {
                showRetireConfirm = false
                scope.launch { controller.retireContentDefinition(detail.definition.id) }
            },
            onDismiss = { showRetireConfirm = false },
        )
    }

    publishTarget?.let { version ->
        PublishDialog(
            state = state,
            controller = controller,
            definitionId = detail.definition.id,
            version = version,
            ownKeys = ownKeys,
            onDismiss = { publishTarget = null },
        )
    }
}

@Composable
private fun VersionRow(version: PlatformContentVersion, onPublish: () -> Unit, canPublish: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(text = stringResource(Res.string.admin_content_version_label, version.version), style = typography.sm, color = tokens.cardForeground)
            version.publishNote?.takeIf { it.isNotBlank() }?.let {
                Text(text = it, style = typography.xs, color = tokens.mutedForeground)
            }
        }
        Badge(variant = if (version.publishedAt != null) BadgeVariant.Default else BadgeVariant.Outline) {
            Text(
                text =
                    if (version.publishedAt != null) stringResource(Res.string.admin_content_version_published)
                    else stringResource(Res.string.admin_content_version_draft),
                style = typography.xs,
            )
        }
        val decision: ManageDecision = if (canPublish) ManageDecision.Allowed
        else ManageDecision.Denied(stringResource(Res.string.admin_content_publish_denied))
        ManageGate(decision = decision) { enabled ->
            GatedButton(enabled = enabled, decision = decision, size = ButtonSize.Sm, onClick = onPublish) {
                Text(text = stringResource(Res.string.admin_content_publish), style = typography.xs)
            }
        }
    }
}

@Composable
private fun DraftVersionDialog(
    initialPayload: String,
    onDismiss: () -> Unit,
    onDraft: (payloadJson: String) -> Unit,
) {
    var payloadJson: String by remember { mutableStateOf(initialPayload) }
    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_content_draft_new_version))
        JsonPayloadField(
            value = payloadJson,
            onValueChange = { payloadJson = it },
            label = stringResource(Res.string.admin_content_payload_label),
        )
        DialogFooter {
            Button(onClick = onDismiss, variant = ButtonVariant.Ghost) {
                Text(text = stringResource(Res.string.admin_cancel))
            }
            Button(onClick = { onDraft(payloadJson) }, enabled = payloadJson.isNotBlank()) {
                Text(text = stringResource(Res.string.admin_content_draft_new_version))
            }
        }
    }
}

/**
 * The publish flow: pick a mode, see the REAL counted blast radius for that exact mode (never a guess),
 * supply a justification if forcing, and only then may the confirm control enable. Selecting a different
 * mode re-runs the preview — the count shown is always for the mode currently selected, never stale.
 */
@Composable
private fun PublishDialog(
    state: AdminState,
    controller: AdminController,
    definitionId: String,
    version: PlatformContentVersion,
    ownKeys: Set<String>?,
    onDismiss: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    var mode: String by remember { mutableStateOf(PlatformContentPublishModes.PublishAsNew) }
    var justification: String by remember { mutableStateOf("") }

    LaunchedEffect(mode) {
        controller.previewContentPublish(definitionId, version.id, mode)
    }

    val canForce: Boolean = ownKeys == null || ContentPermissions.PublishForce in ownKeys
    val preview: PublishPreview? = state.publishPreview
    val isForce: Boolean = mode == PlatformContentPublishModes.Force
    val justificationOk: Boolean = !isForce || justification.trim().isNotEmpty()
    // The confirm gate: a fresh preview for THIS mode must have landed, and (for force) a justification
    // must be present. This is the client-side half of the same rule the server enforces with
    // PREVIEW_STALE — the button simply never offers a submit the server would reject.
    val confirmEnabled: Boolean =
        preview != null && !state.publishPreviewLoading && !state.publishSubmitting && justificationOk

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_content_publish_title, definitionId.takeLast(6), version.version))

        PublishModeOption(
            label = stringResource(Res.string.admin_content_publish_mode_new),
            description = stringResource(Res.string.admin_content_publish_mode_new_desc),
            selected = mode == PlatformContentPublishModes.PublishAsNew,
            onSelect = { mode = PlatformContentPublishModes.PublishAsNew },
        )
        PublishModeOption(
            label = stringResource(Res.string.admin_content_publish_mode_update),
            description = stringResource(Res.string.admin_content_publish_mode_update_desc),
            selected = mode == PlatformContentPublishModes.UpdateInPlaceWhereUntouched,
            onSelect = { mode = PlatformContentPublishModes.UpdateInPlaceWhereUntouched },
        )
        val forceDecision: ManageDecision = if (canForce) ManageDecision.Allowed
        else ManageDecision.Denied(stringResource(Res.string.admin_content_force_denied))
        ManageGate(decision = forceDecision) { enabled ->
            PublishModeOption(
                label = stringResource(Res.string.admin_content_publish_mode_force),
                description = stringResource(Res.string.admin_content_publish_mode_force_desc),
                selected = mode == PlatformContentPublishModes.Force,
                onSelect = { if (enabled) mode = PlatformContentPublishModes.Force },
                enabled = enabled,
            )
        }

        Spacer(modifier = Modifier.height(spacing.s2))

        // The blast radius — the whole point of this dialog (§2.1). Rendered from the REAL preview
        // endpoint's counts, never a placeholder or an assumed zero.
        when {
            state.publishPreviewLoading ->
                Text(text = stringResource(Res.string.admin_content_preview_loading), style = typography.sm, color = tokens.mutedForeground)
            preview != null -> {
                Text(
                    text = stringResource(Res.string.admin_content_preview_affected, preview.affectedCount),
                    style = typography.sm,
                    color = tokens.foreground,
                )
                if (mode != PlatformContentPublishModes.PublishAsNew) {
                    Text(
                        text = stringResource(Res.string.admin_content_preview_skipped, preview.skippedCount),
                        style = typography.sm,
                        color = if (preview.skippedCount > 0) tokens.accent else tokens.mutedForeground,
                    )
                }
                if (preview.sampleTenantNames.isNotEmpty()) {
                    Text(
                        text = stringResource(Res.string.admin_content_sample_tenants, preview.sampleTenantNames.joinToString(", ")),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
            }
            state.publishPreviewError != null -> ActionErrorBanner(message = state.publishPreviewError)
            else -> Text(text = stringResource(Res.string.admin_content_preview_none), style = typography.sm, color = tokens.mutedForeground)
        }

        if (isForce) {
            Spacer(modifier = Modifier.height(spacing.s2))
            AppTextField(
                value = justification,
                onValueChange = { justification = it },
                label = stringResource(Res.string.admin_content_publish_force_justification_label),
                isError = justification.isBlank(),
                supportingText =
                    if (justification.isBlank()) stringResource(Res.string.admin_content_publish_force_justification_required)
                    else null,
                modifier = Modifier.fillMaxWidth(),
            )
        }

        state.publishError?.let { ActionErrorBanner(message = it) }

        DialogFooter {
            Button(onClick = onDismiss, variant = ButtonVariant.Ghost) {
                Text(text = stringResource(Res.string.admin_cancel))
            }
            Button(
                onClick = {
                    val affected: Int = preview?.affectedCount ?: return@Button
                    scope.launch {
                        controller.publishContentVersion(
                            definitionId = definitionId,
                            versionId = version.id,
                            mode = mode,
                            publishNote = justification.takeIf { isForce },
                            confirmedAffectedCount = affected,
                        )
                        onDismiss()
                    }
                },
                enabled = confirmEnabled,
            ) {
                Text(
                    text =
                        if (state.publishSubmitting) stringResource(Res.string.admin_content_publish_submitting)
                        else stringResource(Res.string.admin_content_publish_confirm),
                )
            }
        }
    }
}

@Composable
private fun PublishModeOption(
    label: String,
    description: String,
    selected: Boolean,
    onSelect: () -> Unit,
    enabled: Boolean = true,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Button(
            onClick = onSelect,
            enabled = enabled,
            variant = if (selected) ButtonVariant.Secondary else ButtonVariant.Outline,
            size = ButtonSize.Sm,
        ) {
            Text(text = label, style = typography.sm)
        }
        Text(
            text = description,
            style = typography.xs,
            color = if (enabled) tokens.mutedForeground else tokens.mutedForeground,
            modifier = Modifier.padding(start = spacing.s2),
        )
    }
}

/** Resolves a permission check into a [ManageDecision]: allowed when [key] is present (or the caller's own
 * keys have not resolved yet — the tab already refused entry above the read floor, so a not-yet-resolved
 * write permission fails open only up to what the server itself will still gate), denied with [reason]
 * otherwise. */
@Composable
private fun manageDecision(ownKeys: Set<String>?, key: String, reason: String): ManageDecision =
    if (ownKeys == null || key in ownKeys) ManageDecision.Allowed else ManageDecision.Denied(reason)

/** A [Button] wrapped for the [ManageGate] pattern: renders disabled with [decision]'s reason as a hover
 * tooltip when denied — the reason is both the accessible `stateDescription` ([ManageGate] sets that) and a
 * visible tooltip, so a sighted operator sees WHY without needing a screen reader. */
@Composable
private fun GatedButton(
    enabled: Boolean,
    decision: ManageDecision,
    variant: ButtonVariant = ButtonVariant.Default,
    size: ButtonSize = ButtonSize.Default,
    onClick: () -> Unit,
    content: @Composable RowScope.() -> Unit,
) {
    val reason: String? = decision.deniedReason?.takeIf { it.isNotBlank() }
    if (reason != null) {
        Tooltip(text = reason) {
            Button(onClick = onClick, enabled = enabled, variant = variant, size = size, content = content)
        }
    } else {
        Button(onClick = onClick, enabled = enabled, variant = variant, size = size, content = content)
    }
}

/**
 * A raw-JSON multi-line field for a content payload. The design-system catalogue has no multi-line text
 * primitive (`AppTextField` is hard-coded single-line) and this slice does not fork the tenant-facing
 * command template editor per the spec's own scope note (§6 S-ADMIN-2 groups that reuse as its own future
 * step) — so this is a minimal, tokens-driven field: every color and radius comes from [LocalTokens], only
 * the text-input mechanics are raw Compose Foundation.
 */
@Composable
private fun JsonPayloadField(value: String, onValueChange: (String) -> Unit, label: String) {
    val tokens: Tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(text = label, style = typography.sm, color = tokens.foreground)
        Box(
            modifier =
                Modifier
                    .fillMaxWidth()
                    .heightIn(min = spacing.s24 + spacing.s6)
                    .clip(RoundedCornerShape(tokens.radius.sm))
                    .background(tokens.muted)
                    .padding(spacing.s3),
        ) {
            BasicTextField(
                value = value,
                onValueChange = onValueChange,
                textStyle = TextStyle(color = tokens.foreground, fontSize = typography.sm.fontSize),
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}

