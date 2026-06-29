// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.quotes.ui

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
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.material3.TextFieldColors
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
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.network.Quote
import bot.nomnomz.dashboard.feature.quotes.state.QuotesController
import bot.nomnomz.dashboard.feature.quotes.state.QuotesState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.quotes_action_error
import nomnomzbot.composeapp.generated.resources.quotes_attribution
import nomnomzbot.composeapp.generated.resources.quotes_delete_action
import nomnomzbot.composeapp.generated.resources.quotes_delete_cancel
import nomnomzbot.composeapp.generated.resources.quotes_delete_confirm
import nomnomzbot.composeapp.generated.resources.quotes_delete_message
import nomnomzbot.composeapp.generated.resources.quotes_delete_title
import nomnomzbot.composeapp.generated.resources.quotes_dialog_cancel
import nomnomzbot.composeapp.generated.resources.quotes_dialog_create
import nomnomzbot.composeapp.generated.resources.quotes_dialog_create_title
import nomnomzbot.composeapp.generated.resources.quotes_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.quotes_dialog_game_label
import nomnomzbot.composeapp.generated.resources.quotes_dialog_name_label
import nomnomzbot.composeapp.generated.resources.quotes_dialog_save
import nomnomzbot.composeapp.generated.resources.quotes_dialog_text_label
import nomnomzbot.composeapp.generated.resources.quotes_edit_action
import nomnomzbot.composeapp.generated.resources.quotes_empty
import nomnomzbot.composeapp.generated.resources.quotes_error
import nomnomzbot.composeapp.generated.resources.quotes_loading
import nomnomzbot.composeapp.generated.resources.quotes_new_action
import nomnomzbot.composeapp.generated.resources.quotes_number
import nomnomzbot.composeapp.generated.resources.quotes_retry
import nomnomzbot.composeapp.generated.resources.quotes_title
import nomnomzbot.composeapp.generated.resources.shell_nav_quotes
import org.jetbrains.compose.resources.stringResource

// The Quotes page (frontend-ia.md §3, Chat group): the channel's numbered quote library, all real data from
// [QuotesController]. The screen is a pure projection of the controller's state; it loads on first
// composition. This is the full management surface — create, edit, and delete — each routed back through the
// controller, which re-lists after every successful write so the page reflects the backend.
@Composable
fun QuotesScreen(controller: QuotesController, role: ManagementRole?) {
    val state: QuotesState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Quotes gates every write control at its single Editor manage floor
    // (frontend-ia.md §3). A caller below it sees the list but every create/edit/delete control disabled with
    // "Requires Editor" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Quotes)

    // The create/edit dialog target: null = closed, a value = open (an empty editor = create, a pre-filled one
    // = edit). The delete-confirm target is the quote pending confirmation, or null when none.
    var editor: QuoteEditor? by remember { mutableStateOf(null) }
    var pendingDelete: Quote? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: QuotesState = state) {
            is QuotesState.Loading -> CenteredMessage(stringResource(Res.string.quotes_loading))
            is QuotesState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is QuotesState.Empty ->
                ManagedContent(
                    quotes = emptyList(),
                    actionError = null,
                    manage = manage,
                    onNew = { editor = QuoteEditor.create() },
                    onEdit = { quote -> editor = QuoteEditor.edit(quote) },
                    onDelete = { quote -> pendingDelete = quote },
                )
            is QuotesState.Ready ->
                ManagedContent(
                    quotes = current.quotes,
                    actionError = current.actionError,
                    manage = manage,
                    onNew = { editor = QuoteEditor.create() },
                    onEdit = { quote -> editor = QuoteEditor.edit(quote) },
                    onDelete = { quote -> pendingDelete = quote },
                )
        }
    }

    editor?.let { open ->
        QuoteFormDialog(
            editor = open,
            onDismiss = { editor = null },
            onSubmit = { text, name, game ->
                editor = null
                scope.launch {
                    if (open.isEdit) controller.updateQuote(open.number, text, name, game)
                    else controller.createQuote(text, name, game)
                }
            },
        )
    }

    pendingDelete?.let { quote ->
        ConfirmDialog(
            title = stringResource(Res.string.quotes_delete_title),
            message = stringResource(Res.string.quotes_delete_message, quote.number),
            confirmLabel = stringResource(Res.string.quotes_delete_confirm),
            dismissLabel = stringResource(Res.string.quotes_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteQuote(quote.number) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

// The list-bearing content: the header with the "+ New quote" action, an optional write-failure banner, and
// either the rows or the empty hint. Shared by the Ready and Empty states so a fresh channel can still create
// its first quote from the same header.
@Composable
private fun ManagedContent(
    quotes: List<Quote>,
    actionError: String?,
    manage: ManageDecision,
    onNew: () -> Unit,
    onEdit: (Quote) -> Unit,
    onDelete: (Quote) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Header(manage = manage, onNew = onNew)
        actionError?.let { ActionErrorBanner(message = stringResource(Res.string.quotes_action_error, it)) }

        if (quotes.isEmpty()) {
            CenteredMessage(stringResource(Res.string.quotes_empty))
        } else {
            QuoteList(quotes = quotes, manage = manage, onEdit = onEdit, onDelete = onDelete)
        }
    }
}

@Composable
private fun Header(manage: ManageDecision, onNew: () -> Unit) {
    val tokens = LocalTokens.current
    val newLabel: String = stringResource(Res.string.quotes_new_action)

    PageHeader(title = stringResource(Res.string.shell_nav_quotes)) {
        ManageGate(decision = manage) { enabled ->
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
private fun QuoteList(
    quotes: List<Quote>,
    manage: ManageDecision,
    onEdit: (Quote) -> Unit,
    onDelete: (Quote) -> Unit,
) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        items(items = quotes, key = { quote -> quote.id }) { quote ->
            QuoteRow(
                quote = quote,
                manage = manage,
                onEdit = { onEdit(quote) },
                onDelete = { onDelete(quote) },
            )
        }
    }
}

@Composable
private fun QuoteRow(quote: Quote, manage: ManageDecision, onEdit: () -> Unit, onDelete: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val number: String = stringResource(Res.string.quotes_number, quote.number)
    val attribution: String? = attributionLine(quote.quotedDisplayName, quote.contextGame)
    val editLabel: String = stringResource(Res.string.quotes_edit_action, quote.number)
    val deleteLabel: String = stringResource(Res.string.quotes_delete_action, quote.number)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                // One node for the text block: "Quote #3. <text>. — name, game".
                .clearAndSetSemantics {
                    contentDescription =
                        "$number. ${quote.text}" + (attribution?.let { ". $it" } ?: "")
                },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = number,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
            Text(
                text = quote.text,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 3,
                overflow = TextOverflow.Ellipsis,
            )
            attribution?.let {
                Text(
                    text = it,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }

        ManageGate(decision = manage) { enabled ->
            GlyphButton(imageVector = EditGlyph, label = editLabel, onClick = onEdit, enabled = enabled)
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
}

// The "— name, game" attribution line, built only from the parts that are present. Null when neither the
// speaker nor the game is known, so the row simply omits the line. The body is assembled first so the single
// composable [stringResource] call is unconditional (no composable call sits behind an early return).
@Composable
private fun attributionLine(name: String?, game: String?): String? {
    val speaker: String? = name?.takeIf { it.isNotBlank() }
    val context: String? = game?.takeIf { it.isNotBlank() }
    val body: String = listOfNotNull(speaker, context).joinToString(", ")
    return if (body.isBlank()) null else stringResource(Res.string.quotes_attribution, body)
}

// One composable for both create and edit (DRY): an empty [editor] = create, a pre-filled one = edit. The
// affirmative button is disabled until the quote text is non-blank, so an empty quote can never be submitted.
// The number is shown read-only on edit (it is the backend's immutable address); attribution is optional.
@Composable
private fun QuoteFormDialog(
    editor: QuoteEditor,
    onDismiss: () -> Unit,
    onSubmit: (text: String, name: String, game: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var text: String by remember { mutableStateOf(editor.text) }
    var name: String by remember { mutableStateOf(editor.quotedDisplayName) }
    var game: String by remember { mutableStateOf(editor.contextGame) }

    val canSubmit: Boolean = text.isNotBlank()
    val title: String =
        stringResource(
            if (editor.isEdit) Res.string.quotes_dialog_edit_title
            else Res.string.quotes_dialog_create_title
        )
    val submitLabel: String =
        stringResource(
            if (editor.isEdit) Res.string.quotes_dialog_save else Res.string.quotes_dialog_create
        )

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                OutlinedTextField(
                    value = text,
                    onValueChange = { text = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.quotes_dialog_text_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = name,
                    onValueChange = { name = it },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.quotes_dialog_name_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = game,
                    onValueChange = { game = it },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.quotes_dialog_game_label)) },
                    colors = fieldColors(),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onSubmit(text, name, game) }, enabled = canSubmit) {
                Text(
                    text = submitLabel,
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    text = stringResource(Res.string.quotes_dialog_cancel),
                    color = tokens.mutedForeground,
                )
            }
        },
    )
}

// The shared text-field color set: every slot driven by a token so the field reads on-theme in light + dark.
@Composable
private fun fieldColors(): TextFieldColors {
    val tokens: Tokens = LocalTokens.current
    return OutlinedTextFieldDefaults.colors(
        focusedTextColor = tokens.cardForeground,
        unfocusedTextColor = tokens.cardForeground,
        disabledTextColor = tokens.mutedForeground,
        focusedBorderColor = tokens.ring,
        unfocusedBorderColor = tokens.border,
        disabledBorderColor = tokens.border,
        focusedLabelColor = tokens.mutedForeground,
        unfocusedLabelColor = tokens.mutedForeground,
        disabledLabelColor = tokens.mutedForeground,
        cursorColor = tokens.primary,
    )
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
                text = stringResource(Res.string.quotes_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.quotes_retry)) }
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

// The create/edit dialog's seed: an empty editor opens a blank create form; one seeded from a quote opens a
// pre-filled edit form. [isEdit] decides create-vs-update on submit; [number] is the backend's immutable
// address used by the update call (0 and unused on create).
private data class QuoteEditor(
    val isEdit: Boolean,
    val number: Int,
    val text: String,
    val quotedDisplayName: String,
    val contextGame: String,
) {
    companion object {
        fun create(): QuoteEditor =
            QuoteEditor(isEdit = false, number = 0, text = "", quotedDisplayName = "", contextGame = "")

        fun edit(quote: Quote): QuoteEditor =
            QuoteEditor(
                isEdit = true,
                number = quote.number,
                text = quote.text,
                quotedDisplayName = quote.quotedDisplayName.orEmpty(),
                contextGame = quote.contextGame.orEmpty(),
            )
    }
}
