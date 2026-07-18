// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.supporters.ui

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
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
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
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.SupporterConnection
import bot.nomnomz.dashboard.core.network.SupporterConnectionMode
import bot.nomnomz.dashboard.core.network.SupporterConnectionStatus
import bot.nomnomz.dashboard.core.network.SupporterEvent
import bot.nomnomz.dashboard.core.network.SupporterEventKind
import bot.nomnomz.dashboard.core.network.SupporterSourceKey
import bot.nomnomz.dashboard.feature.supporters.state.ConnectionsState
import bot.nomnomz.dashboard.feature.supporters.state.EventsState
import bot.nomnomz.dashboard.feature.supporters.state.SupportersAccess
import bot.nomnomz.dashboard.feature.supporters.state.SupportersController
import bot.nomnomz.dashboard.feature.supporters.state.formatSupporterAmount
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_nav_supporters
import nomnomzbot.composeapp.generated.resources.supporters_action_error
import nomnomzbot.composeapp.generated.resources.supporters_cancel
import nomnomzbot.composeapp.generated.resources.supporters_connect_action
import nomnomzbot.composeapp.generated.resources.supporters_copied
import nomnomzbot.composeapp.generated.resources.supporters_copy
import nomnomzbot.composeapp.generated.resources.supporters_ingest_url_label
import nomnomzbot.composeapp.generated.resources.supporters_secret_label
import nomnomzbot.composeapp.generated.resources.supporters_connections_error
import nomnomzbot.composeapp.generated.resources.supporters_connections_helper
import nomnomzbot.composeapp.generated.resources.supporters_connections_loading
import nomnomzbot.composeapp.generated.resources.supporters_connections_retry
import nomnomzbot.composeapp.generated.resources.supporters_connections_title
import nomnomzbot.composeapp.generated.resources.supporters_disconnect_action
import nomnomzbot.composeapp.generated.resources.supporters_disconnect_confirm
import nomnomzbot.composeapp.generated.resources.supporters_disconnect_message
import nomnomzbot.composeapp.generated.resources.supporters_disconnect_title
import nomnomzbot.composeapp.generated.resources.supporters_enabled_label
import nomnomzbot.composeapp.generated.resources.supporters_event_recurring
import nomnomzbot.composeapp.generated.resources.supporters_event_tier
import nomnomzbot.composeapp.generated.resources.supporters_event_quantity
import nomnomzbot.composeapp.generated.resources.supporters_events_empty
import nomnomzbot.composeapp.generated.resources.supporters_events_error
import nomnomzbot.composeapp.generated.resources.supporters_events_loading
import nomnomzbot.composeapp.generated.resources.supporters_events_none_match
import nomnomzbot.composeapp.generated.resources.supporters_events_retry
import nomnomzbot.composeapp.generated.resources.supporters_events_title
import nomnomzbot.composeapp.generated.resources.supporters_filter_all
import nomnomzbot.composeapp.generated.resources.supporters_helper
import nomnomzbot.composeapp.generated.resources.supporters_kind_charity
import nomnomzbot.composeapp.generated.resources.supporters_kind_membership
import nomnomzbot.composeapp.generated.resources.supporters_kind_merch
import nomnomzbot.composeapp.generated.resources.supporters_kind_tip
import nomnomzbot.composeapp.generated.resources.supporters_kofi_description
import nomnomzbot.composeapp.generated.resources.supporters_kofi_webhook_hint
import nomnomzbot.composeapp.generated.resources.supporters_last_event
import nomnomzbot.composeapp.generated.resources.supporters_last_event_never
import nomnomzbot.composeapp.generated.resources.supporters_next_page
import nomnomzbot.composeapp.generated.resources.supporters_page_indicator
import nomnomzbot.composeapp.generated.resources.supporters_prev_page
import nomnomzbot.composeapp.generated.resources.supporters_provider_kofi
import nomnomzbot.composeapp.generated.resources.supporters_requires_config
import nomnomzbot.composeapp.generated.resources.supporters_status_active
import nomnomzbot.composeapp.generated.resources.supporters_status_disabled
import nomnomzbot.composeapp.generated.resources.supporters_status_idle
import nomnomzbot.composeapp.generated.resources.supporters_status_not_connected
import org.jetbrains.compose.resources.stringResource

// The Supporters page (supporter-events.md §5, item 13 slice 13a — the Connect/monetization group): the channel's
// monetization CONNECTIONS + the recorded supporter-event FEED, all real data from [SupportersController]. The
// screen is a pure projection of the controller's two states; it loads on first composition.
//
// Part 1 — Connections: one tile per SUPPORTED provider (this slice: Ko-fi, a webhook source), merged with its
// backend connection row. Connect / enable-toggle / disconnect are Broadcaster-only and Critical (they add or
// remove a money source), gated on `supporters:config:write` via disable-with-reason [ManageGate]; a Moderator
// reads the page but its write controls disable. Part 2 — Events: a paged, kind-filterable feed of recorded
// supporter events with amounts rendered in MAJOR units. The backend re-checks every write regardless — the gate
// is UX only.
@Composable
fun SupportersScreen(controller: SupportersController, heldActionKeys: Set<String>) {
    val connections: ConnectionsState by controller.connections.collectAsStateWithLifecycle()
    val events: EventsState by controller.events.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val canConfig: Boolean = SupportersAccess.canConfig(heldActionKeys)
    val configManage: ManageDecision =
        if (canConfig) ManageDecision.Allowed
        else ManageDecision.Denied(stringResource(Res.string.supporters_requires_config))

    // The event feed's filter + page are screen-held UI state (the controller stays filter-agnostic): a kind
    // filter (null = all) and the 1-based page. Changing the filter resets to page 1. The LaunchedEffect below
    // reloads whenever either changes — including the first composition (kind = null, page = 1 = the initial load).
    var kindFilter: String? by remember { mutableStateOf(null) }
    var page: Int by remember { mutableStateOf(1) }
    var pendingDisconnect: SupporterProvider? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.loadConnections() }
    LaunchedEffect(kindFilter, page) { controller.loadEvents(page = page, kind = kindFilter) }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        Column(
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            PageHeader(title = stringResource(Res.string.shell_nav_supporters))

            // The content area takes the height left below the pinned header (weight) so its own scroll region is
            // bounded to the viewport.
            Box(modifier = Modifier.weight(1f).fillMaxWidth()) {
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    Text(
                        text = stringResource(Res.string.supporters_helper),
                        style = LocalTypography.current.sm,
                        color = LocalTokens.current.mutedForeground,
                    )

                    ConnectionsSection(
                        state = connections,
                        configManage = configManage,
                        onRetry = { scope.launch { controller.loadConnections() } },
                        onSetEnabled = { provider, enabled ->
                            scope.launch {
                                controller.upsertConnection(provider.sourceKey, provider.connectionMode, enabled)
                            }
                        },
                        onConnect = { provider, secret ->
                            scope.launch {
                                controller.upsertConnection(
                                    provider.sourceKey,
                                    provider.connectionMode,
                                    isEnabled = true,
                                    authSecret = secret,
                                )
                            }
                        },
                        onDisconnect = { provider -> pendingDisconnect = provider },
                    )

                    EventsSection(
                        state = events,
                        kindFilter = kindFilter,
                        onSelectKind = {
                            kindFilter = it
                            page = 1
                        },
                        onPrev = { if (page > 1) page -= 1 },
                        onNext = { page += 1 },
                        onRetry = { scope.launch { controller.loadEvents(page = page, kind = kindFilter) } },
                    )
                }
            }
        }
    }

    pendingDisconnect?.let { provider ->
        val name: String = providerName(provider.sourceKey)
        ConfirmDialog(
            title = stringResource(Res.string.supporters_disconnect_title),
            message = stringResource(Res.string.supporters_disconnect_message, name),
            confirmLabel = stringResource(Res.string.supporters_disconnect_confirm),
            dismissLabel = stringResource(Res.string.supporters_cancel),
            destructive = true,
            onConfirm = {
                pendingDisconnect = null
                scope.launch { controller.disconnect(provider.sourceKey) }
            },
            onDismiss = { pendingDisconnect = null },
        )
    }
}

// ── Connections ────────────────────────────────────────────────────────────────

@Composable
private fun ConnectionsSection(
    state: ConnectionsState,
    configManage: ManageDecision,
    onRetry: () -> Unit,
    onSetEnabled: (SupporterProvider, Boolean) -> Unit,
    onConnect: (SupporterProvider, String) -> Unit,
    onDisconnect: (SupporterProvider) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.supporters_connections_title),
            style = typography.lg,
            color = tokens.foreground,
        )
        Text(
            text = stringResource(Res.string.supporters_connections_helper),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        Card(modifier = Modifier.fillMaxWidth()) {
            when (state) {
                is ConnectionsState.Loading ->
                    Placeholder(stringResource(Res.string.supporters_connections_loading))
                is ConnectionsState.Error ->
                    ErrorContent(
                        message = stringResource(Res.string.supporters_connections_error, state.detail),
                        retryLabel = stringResource(Res.string.supporters_connections_retry),
                        onRetry = onRetry,
                    )
                is ConnectionsState.Ready ->
                    Column(modifier = Modifier.fillMaxWidth()) {
                        state.actionError?.let {
                            ActionErrorBanner(message = stringResource(Res.string.supporters_action_error, it))
                        }
                        // One tile per SUPPORTED provider (this slice: Ko-fi only), merged with its backend row.
                        // We render the adapter set — never a hardcoded grid of not-yet-live providers.
                        SupportedProviders.forEachIndexed { index, provider ->
                            val connection: SupporterConnection? =
                                state.connections.firstOrNull { it.sourceKey == provider.sourceKey }
                            ProviderTile(
                                provider = provider,
                                connection = connection,
                                configManage = configManage,
                                onSetEnabled = { enabled -> onSetEnabled(provider, enabled) },
                                onConnect = { secret -> onConnect(provider, secret) },
                                onDisconnect = { onDisconnect(provider) },
                            )
                            if (index < SupportedProviders.lastIndex) Separator()
                        }
                    }
            }
        }
    }
}

// One provider tile: the provider's name + a status badge, its description, and the write controls. When
// connected it shows the enforced enable-toggle (Switch), the last-event line, and a Disconnect; when not
// connected it shows the webhook setup hint and a Connect. Every write control is gated by [configManage].
@Composable
private fun ProviderTile(
    provider: SupporterProvider,
    connection: SupporterConnection?,
    configManage: ManageDecision,
    onSetEnabled: (Boolean) -> Unit,
    onConnect: (String) -> Unit,
    onDisconnect: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = providerName(provider.sourceKey)
    // The verification secret typed into the not-connected form (write-only; never echoed back once stored).
    var secret: String by remember(provider.sourceKey) { mutableStateOf("") }

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            StatusBadge(connection = connection)
        }

        Text(
            text = providerDescription(provider.sourceKey),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        // Branch on `connection != null` (not a derived Boolean) so Kotlin smart-casts it to non-null below.
        if (connection != null) {
            // The enforced enable-toggle: ingest for this provider only fires while it is enabled (backend
            // default-deny). Gated on config; a Moderator sees it disabled with a reason.
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                Text(
                    text = stringResource(Res.string.supporters_enabled_label),
                    style = typography.sm,
                    color = tokens.foreground,
                    modifier = Modifier.weight(1f),
                )
                ManageGate(decision = configManage) { enabled ->
                    Switch(
                        checked = connection.isEnabled,
                        onCheckedChange = onSetEnabled,
                        enabled = enabled,
                    )
                }
            }
            Text(
                text = lastEventText(connection.lastEventAt),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            // The one-step-provisioned ingest URL: paste it into the provider's webhook settings. Only shown when
            // the backend returned one (a webhook connection with a provisioned endpoint).
            connection.endpointUrl?.takeIf { it.isNotBlank() }?.let { url ->
                Text(
                    text = stringResource(Res.string.supporters_ingest_url_label),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                CopyValue(
                    value = url,
                    copyLabel = stringResource(Res.string.supporters_copy),
                    copiedLabel = stringResource(Res.string.supporters_copied),
                )
            }
            ManageGate(decision = configManage) { enabled ->
                GlyphButton(
                    imageVector = TrashGlyph,
                    label = stringResource(Res.string.supporters_disconnect_action, name),
                    onClick = onDisconnect,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
        } else {
            // Not connected: collect the provider's verification token and connect in ONE step — the backend
            // auto-provisions the inbound ingest endpoint from the secret and returns its URL (shown above once
            // connected). No separate trip to the Webhooks page.
            Text(
                text = stringResource(Res.string.supporters_kofi_webhook_hint),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            ManageGate(decision = configManage) { enabled ->
                AppTextField(
                    value = secret,
                    onValueChange = { secret = it },
                    label = stringResource(Res.string.supporters_secret_label),
                    enabled = enabled,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            ManageGate(decision = configManage) { enabled ->
                Button(
                    onClick = { onConnect(secret) },
                    variant = ButtonVariant.Default,
                    size = ButtonSize.Sm,
                    enabled = enabled && secret.isNotBlank(),
                ) {
                    Text(text = stringResource(Res.string.supporters_connect_action))
                }
            }
        }
    }
}

@Composable
private fun StatusBadge(connection: SupporterConnection?) {
    val variant: BadgeVariant
    val label: String
    when {
        connection == null -> {
            variant = BadgeVariant.Outline
            label = stringResource(Res.string.supporters_status_not_connected)
        }
        !connection.isEnabled -> {
            variant = BadgeVariant.Outline
            label = stringResource(Res.string.supporters_status_disabled)
        }
        connection.status == SupporterConnectionStatus.Active -> {
            variant = BadgeVariant.Default
            label = stringResource(Res.string.supporters_status_active)
        }
        else -> {
            variant = BadgeVariant.Secondary
            label = stringResource(Res.string.supporters_status_idle)
        }
    }
    Badge(variant = variant) { Text(text = label) }
}

@Composable
private fun lastEventText(lastEventAt: String?): String {
    val at: String? = lastEventAt?.takeIf { it.isNotBlank() }
    return if (at != null) stringResource(Res.string.supporters_last_event, at)
    else stringResource(Res.string.supporters_last_event_never)
}

// ── Events feed ──────────────────────────────────────────────────────────────

@Composable
private fun EventsSection(
    state: EventsState,
    kindFilter: String?,
    onSelectKind: (String?) -> Unit,
    onPrev: () -> Unit,
    onNext: () -> Unit,
    onRetry: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.supporters_events_title),
            style = typography.lg,
            color = tokens.foreground,
        )

        when (state) {
            is EventsState.Loading ->
                Card(modifier = Modifier.fillMaxWidth()) {
                    Placeholder(stringResource(Res.string.supporters_events_loading))
                }
            is EventsState.Empty ->
                Card(modifier = Modifier.fillMaxWidth()) {
                    Placeholder(stringResource(Res.string.supporters_events_empty))
                }
            is EventsState.Error ->
                Card(modifier = Modifier.fillMaxWidth()) {
                    ErrorContent(
                        message = stringResource(Res.string.supporters_events_error, state.detail),
                        retryLabel = stringResource(Res.string.supporters_events_retry),
                        onRetry = onRetry,
                    )
                }
            is EventsState.Ready -> {
                KindFilterRow(selected = kindFilter, onSelect = onSelectKind)
                Card(modifier = Modifier.fillMaxWidth()) {
                    if (state.events.isEmpty()) {
                        Placeholder(stringResource(Res.string.supporters_events_none_match))
                    } else {
                        Column(modifier = Modifier.fillMaxWidth()) {
                            state.events.forEachIndexed { index, event ->
                                EventRow(event = event)
                                if (index < state.events.lastIndex) Separator()
                            }
                        }
                    }
                }
                Pager(page = state.page, hasMore = state.hasMore, onPrev = onPrev, onNext = onNext)
            }
        }
    }
}

// The kind filter: All + the four documented supporter kinds (supporter-events.md §5). This slice only Ko-fi is
// live (it emits tip / membership / merch), so `charity` yields an empty page today — the filter reflects the
// documented domain, not the current adapter's subset. No source filter is shown while a single adapter exists.
@Composable
private fun KindFilterRow(selected: String?, onSelect: (String?) -> Unit) {
    val spacing = LocalSpacing.current

    FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
        SelectChip(
            label = stringResource(Res.string.supporters_filter_all),
            selected = selected == null,
            onClick = { onSelect(null) },
        )
        KindFilters.forEach { kind ->
            SelectChip(
                label = kindLabel(kind),
                selected = selected == kind,
                onClick = { onSelect(kind) },
            )
        }
    }
}

@Composable
private fun EventRow(event: SupporterEvent) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = event.supporterDisplayName,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (event.isRecurring) {
                Badge(variant = BadgeVariant.Outline) {
                    Text(text = stringResource(Res.string.supporters_event_recurring))
                }
            }
            Badge(variant = BadgeVariant.Secondary) { Text(text = kindLabel(event.kind)) }
        }

        val meta: String = eventMeta(event)
        if (meta.isNotBlank()) {
            Text(
                text = meta,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }

        event.messageText?.takeIf { it.isNotBlank() }?.let {
            Text(
                text = it,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

// The muted meta line: the amount in MAJOR units (amountMinor/100 + currency), the tier, the merch quantity, and
// when it was received — the present pieces joined by " · ".
@Composable
private fun eventMeta(event: SupporterEvent): String {
    val amount: String? = formatSupporterAmount(event.amountMinor, event.currency)
    val tier: String? =
        event.tier?.takeIf { it.isNotBlank() }?.let { stringResource(Res.string.supporters_event_tier, it) }
    val quantity: String? =
        event.quantity?.takeIf { it > 0 }?.let { stringResource(Res.string.supporters_event_quantity, it) }
    val received: String? = event.receivedAt.takeIf { it.isNotBlank() }
    return listOfNotNull(amount, tier, quantity, received).joinToString(" · ")
}

@Composable
private fun Pager(page: Int, hasMore: Boolean, onPrev: () -> Unit, onNext: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Button(
            onClick = onPrev,
            variant = ButtonVariant.Outline,
            size = ButtonSize.Sm,
            enabled = page > 1,
        ) {
            Text(text = stringResource(Res.string.supporters_prev_page))
        }
        Text(
            text = stringResource(Res.string.supporters_page_indicator, page),
            style = typography.sm,
            color = tokens.mutedForeground,
            modifier = Modifier.weight(1f),
            textAlign = TextAlign.Center,
        )
        Button(
            onClick = onNext,
            variant = ButtonVariant.Outline,
            size = ButtonSize.Sm,
            enabled = hasMore,
        ) {
            Text(text = stringResource(Res.string.supporters_next_page))
        }
    }
}

// ── Shared bits ────────────────────────────────────────────────────────────────

@Composable
private fun Placeholder(text: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxWidth().padding(spacing.s6), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground, textAlign = TextAlign.Center)
    }
}

@Composable
private fun ErrorContent(message: String, retryLabel: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxWidth().padding(spacing.s6), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(text = message, style = typography.base, color = tokens.mutedForeground, textAlign = TextAlign.Center)
            TextButton(onClick = onRetry) { Text(text = retryLabel) }
        }
    }
}

// A selectable chip (shadcn Badge in its selectable/toggle form) — one option in the kind filter.
@Composable
private fun SelectChip(label: String, selected: Boolean, onClick: () -> Unit) {
    Badge(selected = selected, onClick = onClick) { Text(text = label) }
}

// ── Provider catalogue + label mappings ──────────────────────────────────────────

// The SUPPORTED supporter providers this slice can wire — the adapter set, not a speculative grid. Ko-fi (a
// webhook source) is the only one live; follow-on slices add their providers here as their adapters ship.
private data class SupporterProvider(val sourceKey: String, val connectionMode: String)

private val SupportedProviders: List<SupporterProvider> =
    listOf(SupporterProvider(SupporterSourceKey.Kofi, SupporterConnectionMode.Webhook))

// The kind values offered by the filter (the documented supporter-event domain).
private val KindFilters: List<String> =
    listOf(
        SupporterEventKind.Tip,
        SupporterEventKind.Membership,
        SupporterEventKind.Merch,
        SupporterEventKind.Charity,
    )

@Composable
private fun providerName(sourceKey: String): String =
    when (sourceKey) {
        SupporterSourceKey.Kofi -> stringResource(Res.string.supporters_provider_kofi)
        else -> sourceKey.replaceFirstChar { it.uppercase() }
    }

@Composable
private fun providerDescription(sourceKey: String): String =
    when (sourceKey) {
        SupporterSourceKey.Kofi -> stringResource(Res.string.supporters_kofi_description)
        else -> ""
    }

@Composable
private fun kindLabel(kind: String): String =
    when (kind) {
        SupporterEventKind.Tip -> stringResource(Res.string.supporters_kind_tip)
        SupporterEventKind.Membership -> stringResource(Res.string.supporters_kind_membership)
        SupporterEventKind.Merch -> stringResource(Res.string.supporters_kind_merch)
        SupporterEventKind.Charity -> stringResource(Res.string.supporters_kind_charity)
        else -> kind.replaceFirstChar { it.uppercase() }
    }
