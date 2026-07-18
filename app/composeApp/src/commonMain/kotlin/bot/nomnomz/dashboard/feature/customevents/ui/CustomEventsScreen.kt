// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.customevents.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.PlayCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.CustomDataSource
import bot.nomnomz.dashboard.core.network.CustomDataSourceOption
import bot.nomnomz.dashboard.core.network.CustomDataSourcePreset
import bot.nomnomz.dashboard.core.network.UpsertCustomDataSourceBody
import bot.nomnomz.dashboard.feature.customevents.state.CustomEventsController
import bot.nomnomz.dashboard.feature.customevents.state.CustomEventsState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.custom_events_action_error
import nomnomzbot.composeapp.generated.resources.custom_events_cancel
import nomnomzbot.composeapp.generated.resources.custom_events_create_title
import nomnomzbot.composeapp.generated.resources.custom_events_delete_cancel
import nomnomzbot.composeapp.generated.resources.custom_events_delete_confirm
import nomnomzbot.composeapp.generated.resources.custom_events_delete_confirm_body
import nomnomzbot.composeapp.generated.resources.custom_events_delete_confirm_title
import nomnomzbot.composeapp.generated.resources.custom_events_edit_title
import nomnomzbot.composeapp.generated.resources.custom_events_empty
import nomnomzbot.composeapp.generated.resources.custom_events_error
import nomnomzbot.composeapp.generated.resources.custom_events_field_auth_secret
import nomnomzbot.composeapp.generated.resources.custom_events_field_display_name
import nomnomzbot.composeapp.generated.resources.custom_events_field_enabled
import nomnomzbot.composeapp.generated.resources.custom_events_field_endpoint_url
import nomnomzbot.composeapp.generated.resources.custom_events_field_field_map_json
import nomnomzbot.composeapp.generated.resources.custom_events_field_name
import nomnomzbot.composeapp.generated.resources.custom_events_field_poll_interval
import nomnomzbot.composeapp.generated.resources.custom_events_field_source_kind
import nomnomzbot.composeapp.generated.resources.custom_events_last_received
import nomnomzbot.composeapp.generated.resources.custom_events_loading
import nomnomzbot.composeapp.generated.resources.custom_events_new_source
import nomnomzbot.composeapp.generated.resources.custom_events_presets_title
import nomnomzbot.composeapp.generated.resources.custom_events_purpose_body
import nomnomzbot.composeapp.generated.resources.custom_events_purpose_example_body
import nomnomzbot.composeapp.generated.resources.custom_events_purpose_example_title
import nomnomzbot.composeapp.generated.resources.custom_events_purpose_title
import nomnomzbot.composeapp.generated.resources.custom_events_search_label
import nomnomzbot.composeapp.generated.resources.custom_events_search_placeholder
import nomnomzbot.composeapp.generated.resources.custom_events_status_disabled
import nomnomzbot.composeapp.generated.resources.custom_events_status_poll
import nomnomzbot.composeapp.generated.resources.custom_events_status_poll_interval
import nomnomzbot.composeapp.generated.resources.custom_events_status_push
import nomnomzbot.composeapp.generated.resources.custom_events_status_socket
import nomnomzbot.composeapp.generated.resources.custom_events_status_waiting
import nomnomzbot.composeapp.generated.resources.custom_events_retry
import nomnomzbot.composeapp.generated.resources.custom_events_save
import nomnomzbot.composeapp.generated.resources.custom_events_secret_placeholder
import nomnomzbot.composeapp.generated.resources.custom_events_subtitle
import nomnomzbot.composeapp.generated.resources.custom_events_test_action
import nomnomzbot.composeapp.generated.resources.custom_events_test_payload_label
import nomnomzbot.composeapp.generated.resources.custom_events_test_success
import nomnomzbot.composeapp.generated.resources.shell_nav_custom_events
import org.jetbrains.compose.resources.stringResource

// The Custom Events page: the channel's external data source integrations (custom-events.md). Each source
// (push / poll / socket ingress) maps a raw JSON payload to named fields that fire a `custom.<name>` pipeline
// trigger and populate `{{custom.<name>.*}}` template vars. Pulsoid and HypeRate ship as presets.
@Composable
fun CustomEventsScreen(controller: CustomEventsController, role: ManagementRole?) {
    val state: CustomEventsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.CustomEvents)

    var editTarget: CustomDataSource? by remember { mutableStateOf(null) }
    var showCreateDialog: Boolean by remember { mutableStateOf(false) }
    var deleteTarget: CustomDataSource? by remember { mutableStateOf(null) }
    var testTarget: CustomDataSource? by remember { mutableStateOf(null) }
    var testSuccess: Boolean by remember { mutableStateOf(false) }
    var presetSeed: CustomDataSourcePreset? by remember { mutableStateOf(null) }
    // Increment on every dialog open to force a fresh Popup lifecycle on the Wasm canvas layer
    // (re-using the same Popup composable after dismiss+reopen causes an invisible-dialog rendering bug).
    var dialogKey: Int by remember { mutableStateOf(0) }

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            PageHeader(
                title = stringResource(Res.string.shell_nav_custom_events),
                subtitle = stringResource(Res.string.custom_events_subtitle),
                modifier = Modifier.weight(1f),
            )
            ManageGate(decision = manage) { enabled ->
                GlyphButton(
                    imageVector = AddGlyph,
                    label = stringResource(Res.string.custom_events_new_source),
                    enabled = enabled,
                    onClick = {
                        presetSeed = null
                        dialogKey++
                        showCreateDialog = true
                    },
                )
            }
        }

        when (val current: CustomEventsState = state) {
            is CustomEventsState.Loading ->
                CenteredMessage(stringResource(Res.string.custom_events_loading))

            is CustomEventsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })

            is CustomEventsState.Ready -> {
                current.actionError?.let { detail ->
                    ActionErrorBanner(message = stringResource(Res.string.custom_events_action_error, detail))
                }

                // What a data source IS + a concrete example — the page was a bare CRUD shell before.
                PurposeCard()

                // Id-picker: search the channel's sources by name (GET .../search) and jump straight to one's
                // editor. Handy once a channel has many sources; degrades to a plain field when the endpoint fails.
                if (current.sources.isNotEmpty()) {
                    SourceSearchPicker(
                        onSearch = { query -> controller.search(query) },
                        onPick = { option ->
                            current.sources.firstOrNull { it.id == option.id }?.let { source ->
                                dialogKey++
                                editTarget = source
                            }
                        },
                    )
                }

                if (current.presets.isNotEmpty()) {
                    PresetsSection(
                        presets = current.presets,
                        manage = manage,
                        onUse = { preset ->
                            presetSeed = preset
                            dialogKey++
                            showCreateDialog = true
                        },
                    )
                    Separator()
                }

                Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
                    if (current.sources.isEmpty()) {
                        Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                            CenteredMessage(stringResource(Res.string.custom_events_empty))
                        }
                    } else {
                        LazyColumn(modifier = Modifier.fillMaxSize()) {
                            itemsIndexed(
                                items = current.sources,
                                key = { _, source -> source.id },
                            ) { index, source ->
                                SourceRow(
                                    source = source,
                                    manage = manage,
                                    onEdit = { dialogKey++; editTarget = source },
                                    onDelete = { dialogKey++; deleteTarget = source },
                                    onTest = { dialogKey++; testTarget = source; testSuccess = false },
                                )
                                if (index < current.sources.lastIndex) {
                                    Separator()
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    key(dialogKey) {
        if (showCreateDialog || editTarget != null) {
            val isEdit: Boolean = editTarget != null
            SourceFormDialog(
                title = stringResource(if (isEdit) Res.string.custom_events_edit_title else Res.string.custom_events_create_title),
                initial = editTarget,
                presetSeed = presetSeed,
                onDismiss = { showCreateDialog = false; editTarget = null; presetSeed = null },
                onSave = { body ->
                    scope.launch {
                        val ok: Boolean =
                            if (isEdit) controller.update(editTarget!!.id, body) != null
                            else controller.create(body) != null
                        if (ok) {
                            showCreateDialog = false
                            editTarget = null
                            presetSeed = null
                        }
                    }
                },
            )
        }

        deleteTarget?.let { target ->
            ConfirmDialog(
                title = stringResource(Res.string.custom_events_delete_confirm_title),
                message = stringResource(Res.string.custom_events_delete_confirm_body, target.displayName),
                confirmLabel = stringResource(Res.string.custom_events_delete_confirm),
                dismissLabel = stringResource(Res.string.custom_events_delete_cancel),
                onConfirm = { scope.launch { controller.delete(target.id) }; deleteTarget = null },
                onDismiss = { deleteTarget = null },
            )
        }

        testTarget?.let { target ->
            TestDialog(
                source = target,
                succeeded = testSuccess,
                onDismiss = { testTarget = null },
                onTest = { payload ->
                    scope.launch {
                        testSuccess = controller.test(target.id, payload)
                    }
                },
            )
        }
    }
}

// ── Purpose / explainer card ───────────────────────────────────────────────────

// Explains, in-page, what a custom data source IS and how it is used — plus a concrete example — so the CRUD
// shell is legible to a first-time operator instead of a bare form.
@Composable
private fun PurposeCard() {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.custom_events_purpose_title),
                style = typography.base,
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(Res.string.custom_events_purpose_body),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(tokens.radius.md))
                    .background(tokens.muted)
                    .padding(spacing.s3),
            ) {
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                    Text(
                        text = stringResource(Res.string.custom_events_purpose_example_title),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                    Text(
                        text = stringResource(Res.string.custom_events_purpose_example_body),
                        style = typography.sm,
                        color = tokens.cardForeground,
                    )
                }
            }
        }
    }
}

// ── Source search / id-picker ───────────────────────────────────────────────────

// An autocomplete over the channel's data sources (the GET .../search endpoint): typing surfaces matching
// {id, name, displayName} options in a dropdown; picking one opens that source's editor.
@Composable
private fun SourceSearchPicker(
    onSearch: suspend (String) -> List<CustomDataSourceOption>,
    onPick: (CustomDataSourceOption) -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    var query: String by remember { mutableStateOf("") }
    var options: List<CustomDataSourceOption> by remember { mutableStateOf(emptyList()) }
    var expanded: Boolean by remember { mutableStateOf(false) }

    Box(modifier = Modifier.fillMaxWidth()) {
        AppTextField(
            value = query,
            onValueChange = { text ->
                query = text
                if (text.isBlank()) {
                    options = emptyList()
                    expanded = false
                } else {
                    scope.launch {
                        options = onSearch(text)
                        expanded = options.isNotEmpty()
                    }
                }
            },
            label = stringResource(Res.string.custom_events_search_label),
            placeholder = stringResource(Res.string.custom_events_search_placeholder),
            modifier = Modifier.fillMaxWidth(),
        )
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            for (option in options) {
                DropdownMenuItem(
                    text = { Text(text = option.displayName, color = tokens.popoverForeground) },
                    onClick = {
                        expanded = false
                        query = ""
                        options = emptyList()
                        onPick(option)
                    },
                )
            }
        }
    }
}

// ── Presets section ───────────────────────────────────────────────────────────

@Composable
private fun PresetsSection(
    presets: List<CustomDataSourcePreset>,
    manage: ManageDecision,
    onUse: (CustomDataSourcePreset) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.custom_events_presets_title),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        LazyRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            items(items = presets, key = { it.key }) { preset ->
                Column(
                    modifier = Modifier
                        .clip(RoundedCornerShape(tokens.radius.lg))
                        .background(tokens.card)
                        .padding(spacing.s3),
                    verticalArrangement = Arrangement.spacedBy(spacing.s2),
                ) {
                    Text(text = preset.displayName, style = typography.sm, color = tokens.cardForeground)
                    Text(text = preset.sourceKind, style = typography.xs, color = tokens.mutedForeground)
                    ManageGate(decision = manage) { enabled ->
                        Button(
                            onClick = { onUse(preset) },
                            enabled = enabled,
                        ) {
                            Text(text = stringResource(Res.string.custom_events_new_source), style = typography.xs)
                        }
                    }
                }
            }
        }
    }
}

// ── Source row ────────────────────────────────────────────────────────────────

@Composable
private fun SourceRow(
    source: CustomDataSource,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
    onTest: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Row(
                    horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = source.displayName,
                        style = typography.base,
                        color = tokens.cardForeground,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    SourceKindBadge(kind = source.sourceKind)
                }
                Text(
                    text = "{{custom.${source.name}.*}}",
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                // Honest, derived status: the ingest kind + whether a payload has actually arrived yet (no
                // fabricated "connected" — every part comes from the real source record).
                Text(
                    text = sourceStatusLine(source),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }

            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(tokens.radius.sm))
                    .background(if (source.isEnabled) tokens.primary.copy(alpha = 0.15f) else tokens.muted)
                    .padding(horizontal = spacing.s2, vertical = spacing.s1),
            ) {
                Text(
                    text = if (source.isEnabled) "on" else "off",
                    style = typography.xs,
                    color = if (source.isEnabled) tokens.primary else tokens.mutedForeground,
                )
            }
        }

        ManageGate(decision = manage) { enabled ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                GlyphButton(imageVector = EditGlyph, label = "Edit", enabled = enabled, onClick = onEdit)
                GlyphButton(imageVector = PlayCircleGlyph, label = "Test", enabled = enabled, onClick = onTest)
                Spacer(modifier = Modifier.weight(1f))
                GlyphButton(imageVector = TrashGlyph, label = "Delete", enabled = enabled, onClick = onDelete)
            }
        }
    }
}

// A one-line status derived entirely from the source record: the ingress kind (poll shows its interval) joined
// with the connection state (disabled / waiting for the first payload / last-received date). Never invents a
// "live" state — only what the backend actually reports.
@Composable
private fun sourceStatusLine(source: CustomDataSource): String {
    val kind: String = when (source.sourceKind) {
        "poll" -> source.pollIntervalSeconds?.let {
            stringResource(Res.string.custom_events_status_poll_interval, it)
        } ?: stringResource(Res.string.custom_events_status_poll)
        "socket" -> stringResource(Res.string.custom_events_status_socket)
        else -> stringResource(Res.string.custom_events_status_push)
    }
    val connection: String = when {
        !source.isEnabled -> stringResource(Res.string.custom_events_status_disabled)
        source.lastReceivedAt == null -> stringResource(Res.string.custom_events_status_waiting)
        else -> stringResource(
            Res.string.custom_events_last_received,
            source.lastReceivedAt.substringBefore('T'),
        )
    }
    return "$kind · $connection"
}

@Composable
private fun SourceKindBadge(kind: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.sm))
            .background(tokens.muted)
            .padding(horizontal = spacing.s2, vertical = spacing.s1),
    ) {
        Text(text = kind, style = typography.xs, color = tokens.mutedForeground)
    }
}

// ── Create / Edit form dialog ─────────────────────────────────────────────────

@Composable
private fun SourceFormDialog(
    title: String,
    initial: CustomDataSource?,
    presetSeed: CustomDataSourcePreset?,
    onDismiss: () -> Unit,
    onSave: (UpsertCustomDataSourceBody) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember(initial, presetSeed) { mutableStateOf(initial?.name ?: presetSeed?.key ?: "") }
    var displayName: String by remember(initial, presetSeed) {
        mutableStateOf(initial?.displayName ?: presetSeed?.displayName ?: "")
    }
    var sourceKind: String by remember(initial, presetSeed) {
        mutableStateOf(initial?.sourceKind ?: presetSeed?.sourceKind ?: "push")
    }
    var endpointUrl: String by remember(initial, presetSeed) { mutableStateOf(initial?.endpointUrl ?: "") }
    var authSecret: String by remember { mutableStateOf("") }
    var fieldMapJson: String by remember(initial) {
        mutableStateOf(
            initial?.fieldMap
                ?.entries
                ?.joinToString(",", "{", "}") { (k, v) -> "\"$k\":\"$v\"" }
                ?: "{}"
        )
    }
    var pollInterval: String by remember(initial) { mutableStateOf(initial?.pollIntervalSeconds?.toString() ?: "") }
    var isEnabled: Boolean by remember(initial) { mutableStateOf(initial?.isEnabled ?: false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title, style = typography.lg) },
        text = {
            LazyColumn(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                item {
                    AppTextField(
                        value = name,
                        onValueChange = { name = it.lowercase().replace(Regex("[^a-z0-9_]"), "") },
                        label = stringResource(Res.string.custom_events_field_name),
                        modifier = Modifier.fillMaxWidth(),
                        enabled = initial == null,
                    )
                }
                item {
                    AppTextField(
                        value = displayName,
                        onValueChange = { displayName = it },
                        label = stringResource(Res.string.custom_events_field_display_name),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                item { SourceKindSelector(selected = sourceKind, onSelect = { sourceKind = it }) }
                item {
                    AppTextField(
                        value = endpointUrl,
                        onValueChange = { endpointUrl = it },
                        label = stringResource(Res.string.custom_events_field_endpoint_url),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                item {
                    AppTextField(
                        value = authSecret,
                        onValueChange = { authSecret = it },
                        label = stringResource(
                            if (initial?.hasAuthSecret == true) Res.string.custom_events_secret_placeholder
                            else Res.string.custom_events_field_auth_secret
                        ),
                        modifier = Modifier.fillMaxWidth(),
                        visualTransformation = PasswordVisualTransformation(),
                    )
                }
                item {
                    Textarea(
                        value = fieldMapJson,
                        onValueChange = { fieldMapJson = it },
                        label = stringResource(Res.string.custom_events_field_field_map_json),
                        modifier = Modifier.fillMaxWidth(),
                        minLines = 2,
                    )
                }
                if (sourceKind == "poll") {
                    item {
                        AppTextField(
                            value = pollInterval,
                            onValueChange = { pollInterval = it.filter { c -> c.isDigit() } },
                            label = stringResource(Res.string.custom_events_field_poll_interval),
                            modifier = Modifier.fillMaxWidth(),
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        )
                    }
                }
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        Text(
                            text = stringResource(Res.string.custom_events_field_enabled),
                            style = typography.base,
                            color = tokens.cardForeground,
                        )
                        Switch(
                            checked = isEnabled,
                            onCheckedChange = { isEnabled = it },
                        )
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    onSave(
                        UpsertCustomDataSourceBody(
                            name = name.trim(),
                            displayName = displayName.trim(),
                            sourceKind = sourceKind,
                            presetKey = presetSeed?.key ?: initial?.presetKey,
                            endpointUrl = endpointUrl.trim().takeIf { it.isNotEmpty() },
                            authSecret = authSecret.takeIf { it.isNotEmpty() },
                            fieldMap = parseFieldMapJson(fieldMapJson),
                            pollIntervalSeconds = pollInterval.toIntOrNull(),
                            isEnabled = isEnabled,
                        )
                    )
                },
                enabled = name.isNotBlank() && displayName.isNotBlank(),
            ) {
                Text(stringResource(Res.string.custom_events_save))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.custom_events_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun SourceKindSelector(selected: String, onSelect: (String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val kinds: List<String> = listOf("push", "poll", "socket")

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(
            text = stringResource(Res.string.custom_events_field_source_kind),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            kinds.forEach { kind ->
                val isSelected: Boolean = kind == selected
                Button(
                    onClick = { onSelect(kind) },
                    variant = if (isSelected) ButtonVariant.Default else ButtonVariant.Outline,
                ) {
                    Text(text = kind, style = typography.sm)
                }
            }
        }
    }
}

// ── Test dialog ───────────────────────────────────────────────────────────────

@Composable
private fun TestDialog(
    source: CustomDataSource,
    succeeded: Boolean,
    onDismiss: () -> Unit,
    onTest: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var payload: String by remember(source) {
        val mapStr: String = source.fieldMap.keys.joinToString(", ") { key -> "\"$key\": 0" }
        mutableStateOf("{$mapStr}")
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = source.displayName, style = typography.lg) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                Textarea(
                    value = payload,
                    onValueChange = { payload = it },
                    label = stringResource(Res.string.custom_events_test_payload_label),
                    modifier = Modifier.fillMaxWidth(),
                    minLines = 2,
                )
                if (succeeded) {
                    Text(
                        text = stringResource(Res.string.custom_events_test_success),
                        style = typography.sm,
                        color = tokens.primary,
                    )
                }
            }
        },
        confirmButton = {
            Button(
                onClick = { onTest(payload) },
            ) {
                Text(stringResource(Res.string.custom_events_test_action))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.custom_events_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// ── Utility composables ───────────────────────────────────────────────────────

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
                text = stringResource(Res.string.custom_events_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.custom_events_retry)) }
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

/** Best-effort parse of `{"key":"$.path"}` JSON into a Map. Returns empty map on any parse failure. */
private fun parseFieldMapJson(json: String): Map<String, String> {
    return try {
        val inner: String = json.trim().removePrefix("{").removeSuffix("}").trim()
        if (inner.isEmpty()) return emptyMap()
        inner.split(",").associate { pair ->
            val (k, v) = pair.trim().split(":", limit = 2)
            k.trim().removeSurrounding("\"") to v.trim().removeSurrounding("\"")
        }
    } catch (_: Exception) {
        emptyMap()
    }
}
