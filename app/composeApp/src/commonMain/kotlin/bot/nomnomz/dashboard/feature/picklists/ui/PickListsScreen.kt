// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.picklists.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
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
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.PickList
import bot.nomnomz.dashboard.feature.picklists.state.PickListsAccess
import bot.nomnomz.dashboard.feature.picklists.state.PickListsController
import bot.nomnomz.dashboard.feature.picklists.state.PickListsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.picklists_action_error
import nomnomzbot.composeapp.generated.resources.picklists_delete_action
import nomnomzbot.composeapp.generated.resources.picklists_delete_cancel
import nomnomzbot.composeapp.generated.resources.picklists_delete_confirm
import nomnomzbot.composeapp.generated.resources.picklists_delete_message
import nomnomzbot.composeapp.generated.resources.picklists_delete_title
import nomnomzbot.composeapp.generated.resources.picklists_dialog_add_item
import nomnomzbot.composeapp.generated.resources.picklists_dialog_cancel
import nomnomzbot.composeapp.generated.resources.picklists_dialog_create
import nomnomzbot.composeapp.generated.resources.picklists_dialog_create_title
import nomnomzbot.composeapp.generated.resources.picklists_dialog_description_label
import nomnomzbot.composeapp.generated.resources.picklists_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.picklists_dialog_item_placeholder
import nomnomzbot.composeapp.generated.resources.picklists_dialog_items_label
import nomnomzbot.composeapp.generated.resources.picklists_dialog_name_label
import nomnomzbot.composeapp.generated.resources.picklists_dialog_remove_item
import nomnomzbot.composeapp.generated.resources.picklists_dialog_save
import nomnomzbot.composeapp.generated.resources.picklists_edit_action
import nomnomzbot.composeapp.generated.resources.picklists_empty
import nomnomzbot.composeapp.generated.resources.picklists_error
import nomnomzbot.composeapp.generated.resources.picklists_helper
import nomnomzbot.composeapp.generated.resources.picklists_item_count
import nomnomzbot.composeapp.generated.resources.picklists_loading
import nomnomzbot.composeapp.generated.resources.picklists_new_action
import nomnomzbot.composeapp.generated.resources.picklists_requires_delete
import nomnomzbot.composeapp.generated.resources.picklists_requires_write
import nomnomzbot.composeapp.generated.resources.picklists_retry
import nomnomzbot.composeapp.generated.resources.shell_nav_pick_lists
import org.jetbrains.compose.resources.stringResource

// The Pick Lists page (frontend-ia.md §3, Chat group): the channel's named pick-lists — the generic primitive
// behind the `{list.pick.<name>}` template variable — all real data from [PickListsController]. The screen is a
// pure projection of the controller's state; it loads on first composition. This is the full management surface —
// create, edit (name / description / the entries themselves), and delete — each routed back through the
// controller, which re-lists after every successful write so the page reflects the backend.
@Composable
fun PickListsScreen(controller: PickListsController, heldActionKeys: Set<String>) {
    val state: PickListsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // Pick Lists gates create/edit and delete on the caller's RESOLVED capability keys, not a management role — so the
    // broadcaster can delegate list-editing to a VIP/Sub by lowering `picklists:write` (and, separately,
    // `picklists:delete`). A caller who holds `picklists:write` but not `picklists:delete` gets create/edit live but
    // delete disabled-with-reason (frontend-ia.md §7); an Editor+ clears both default floors and so holds both keys.
    // The backend re-checks every write regardless — the gate is UX only. The reasons resolve unconditionally so no
    // composable sits behind a branch.
    val writeManage: ManageDecision =
        if (PickListsAccess.canWrite(heldActionKeys)) ManageDecision.Allowed
        else ManageDecision.Denied(stringResource(Res.string.picklists_requires_write))
    val deleteManage: ManageDecision =
        if (PickListsAccess.canDelete(heldActionKeys)) ManageDecision.Allowed
        else ManageDecision.Denied(stringResource(Res.string.picklists_requires_delete))

    // The create/edit dialog target: null = closed, a value = open (an empty editor = create, a pre-filled one
    // = edit). The delete-confirm target is the list pending confirmation, or null when none.
    var editor: PickListEditor? by remember { mutableStateOf(null) }
    var pendingDelete: PickList? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: PickListsState = state) {
            is PickListsState.Loading -> CenteredMessage(stringResource(Res.string.picklists_loading))
            is PickListsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is PickListsState.Empty ->
                ManagedContent(
                    lists = emptyList(),
                    actionError = null,
                    writeManage = writeManage,
                    deleteManage = deleteManage,
                    onNew = { editor = PickListEditor.create() },
                    onEdit = { list -> editor = PickListEditor.edit(list) },
                    onDelete = { list -> pendingDelete = list },
                )
            is PickListsState.Ready ->
                ManagedContent(
                    lists = current.lists,
                    actionError = current.actionError,
                    writeManage = writeManage,
                    deleteManage = deleteManage,
                    onNew = { editor = PickListEditor.create() },
                    onEdit = { list -> editor = PickListEditor.edit(list) },
                    onDelete = { list -> pendingDelete = list },
                )
        }
    }

    editor?.let { open ->
        PickListFormDialog(
            editor = open,
            onDismiss = { editor = null },
            onSubmit = { name, description, items ->
                editor = null
                scope.launch {
                    if (open.isEdit) controller.updatePickList(open.id, name, description, items)
                    else controller.createPickList(name, description, items)
                }
            },
        )
    }

    pendingDelete?.let { list ->
        ConfirmDialog(
            title = stringResource(Res.string.picklists_delete_title),
            message = stringResource(Res.string.picklists_delete_message, list.name),
            confirmLabel = stringResource(Res.string.picklists_delete_confirm),
            dismissLabel = stringResource(Res.string.picklists_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deletePickList(list.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

// The list-bearing content: the header with the "+ New pick list" action, the `{list.pick.<name>}` helper line, an
// optional write-failure banner, and one Card wrapping either the rows (with Separators) or the empty hint. Shared
// by the Ready and Empty states so a fresh channel can still create its first list from the same header.
@Composable
private fun ManagedContent(
    lists: List<PickList>,
    actionError: String?,
    writeManage: ManageDecision,
    deleteManage: ManageDecision,
    onNew: () -> Unit,
    onEdit: (PickList) -> Unit,
    onDelete: (PickList) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Header(writeManage = writeManage, onNew = onNew)
        // How the lists are actually used — a one-liner so the operator knows what a pick-list is FOR.
        Text(
            text = stringResource(Res.string.picklists_helper),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        actionError?.let { ActionErrorBanner(message = stringResource(Res.string.picklists_action_error, it)) }

        Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
            if (lists.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        text = stringResource(Res.string.picklists_empty),
                        style = typography.base,
                        color = tokens.mutedForeground,
                    )
                }
            } else {
                LazyColumn(modifier = Modifier.fillMaxSize()) {
                    itemsIndexed(items = lists, key = { _, list -> list.id }) { index, list ->
                        PickListRow(
                            list = list,
                            writeManage = writeManage,
                            deleteManage = deleteManage,
                            onEdit = { onEdit(list) },
                            onDelete = { onDelete(list) },
                        )
                        if (index < lists.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun Header(writeManage: ManageDecision, onNew: () -> Unit) {
    val newLabel: String = stringResource(Res.string.picklists_new_action)

    PageHeader(title = stringResource(Res.string.shell_nav_pick_lists)) {
        ManageGate(decision = writeManage) { enabled ->
            GlyphButton(
                imageVector = AddGlyph,
                label = newLabel,
                onClick = onNew,
                enabled = enabled,
            )
        }
    }
}

@Composable
private fun PickListRow(
    list: PickList,
    writeManage: ManageDecision,
    deleteManage: ManageDecision,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val count: String = stringResource(Res.string.picklists_item_count, list.items.size)
    val description: String? = list.description?.takeIf { it.isNotBlank() }
    val editLabel: String = stringResource(Res.string.picklists_edit_action, list.name)
    val deleteLabel: String = stringResource(Res.string.picklists_delete_action, list.name)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                // One node for the text block: "<name>. N items. <description>".
                .clearAndSetSemantics {
                    contentDescription =
                        "${list.name}. $count" + (description?.let { ". $it" } ?: "")
                },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = list.name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = count,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
            description?.let {
                Text(
                    text = it,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }

        ManageGate(decision = writeManage) { enabled ->
            GlyphButton(imageVector = EditGlyph, label = editLabel, onClick = onEdit, enabled = enabled)
        }
        ManageGate(decision = deleteManage) { enabled ->
            GlyphButton(
                imageVector = TrashGlyph,
                label = deleteLabel,
                onClick = onDelete,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

// One composable for both create and edit (DRY): an empty [editor] = create, a pre-filled one = edit. The
// affirmative button is disabled until the name is non-blank, so a nameless list can never be submitted. The
// [items] are edited in place — add a row, edit a row, remove a row — and passed to the controller, which trims
// them and drops the blanks before they go over the wire.
@Composable
private fun PickListFormDialog(
    editor: PickListEditor,
    onDismiss: () -> Unit,
    onSubmit: (name: String, description: String, items: List<String>) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf(editor.name) }
    var description: String by remember { mutableStateOf(editor.description) }
    var items: List<String> by remember { mutableStateOf(editor.items) }

    val canSubmit: Boolean = name.isNotBlank()
    val title: String =
        stringResource(
            if (editor.isEdit) Res.string.picklists_dialog_edit_title
            else Res.string.picklists_dialog_create_title
        )
    val submitLabel: String =
        stringResource(
            if (editor.isEdit) Res.string.picklists_dialog_save else Res.string.picklists_dialog_create
        )
    val itemPlaceholder: String = stringResource(Res.string.picklists_dialog_item_placeholder)
    val removeItemLabel: String = stringResource(Res.string.picklists_dialog_remove_item)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.picklists_dialog_name_label),
                )
                AppTextField(
                    value = description,
                    onValueChange = { description = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.picklists_dialog_description_label),
                )

                // The entries editor: the "Items" section label, one single-line field per entry (with a per-row
                // delete), then an "Add item" action that appends a fresh blank row. The list is bounded and scrolls
                // so a long list never grows the dialog past the viewport (the parent Dialog Column does not scroll).
                Text(
                    text = stringResource(Res.string.picklists_dialog_items_label),
                    style = typography.sm,
                    color = tokens.foreground,
                )
                if (items.isNotEmpty()) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .heightIn(max = spacing.s24 * 2)
                            .verticalScroll(rememberScrollState()),
                        verticalArrangement = Arrangement.spacedBy(spacing.s2),
                    ) {
                        items.forEachIndexed { index, value ->
                            ItemRow(
                                value = value,
                                placeholder = itemPlaceholder,
                                removeLabel = removeItemLabel,
                                onValueChange = { edited ->
                                    items = items.toMutableList().also { it[index] = edited }
                                },
                                onRemove = { items = items.filterIndexed { i, _ -> i != index } },
                            )
                        }
                    }
                }
                TextButton(onClick = { items = items + "" }) {
                    Text(
                        text = stringResource(Res.string.picklists_dialog_add_item),
                        color = tokens.primary,
                    )
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { onSubmit(name, description, items) }, enabled = canSubmit) {
                Text(
                    text = submitLabel,
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    text = stringResource(Res.string.picklists_dialog_cancel),
                    color = tokens.mutedForeground,
                )
            }
        },
    )
}

// One entry in the items editor: a single-line field (its value hoisted into the parent's [items] list, so the
// row is fully controlled and never keeps stale text when a row above it is removed) plus a destructive remove
// button carrying the entry's accessible name.
@Composable
private fun ItemRow(
    value: String,
    placeholder: String,
    removeLabel: String,
    onValueChange: (String) -> Unit,
    onRemove: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        AppTextField(
            value = value,
            onValueChange = onValueChange,
            label = "",
            placeholder = placeholder,
            modifier = Modifier.weight(1f),
        )
        GlyphButton(
            imageVector = TrashGlyph,
            label = removeLabel,
            onClick = onRemove,
            tint = tokens.destructive,
        )
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
                text = stringResource(Res.string.picklists_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.picklists_retry)) }
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

// The create/edit dialog's seed: an empty editor opens a blank create form; one seeded from a list opens a
// pre-filled edit form. [isEdit] decides create-vs-update on submit; [id] is the backend's opaque address used by
// the update call (blank and unused on create).
private data class PickListEditor(
    val isEdit: Boolean,
    val id: String,
    val name: String,
    val description: String,
    val items: List<String>,
) {
    companion object {
        fun create(): PickListEditor =
            PickListEditor(isEdit = false, id = "", name = "", description = "", items = emptyList())

        fun edit(list: PickList): PickListEditor =
            PickListEditor(
                isEdit = true,
                id = list.id,
                name = list.name,
                description = list.description.orEmpty(),
                items = list.items,
            )
    }
}
