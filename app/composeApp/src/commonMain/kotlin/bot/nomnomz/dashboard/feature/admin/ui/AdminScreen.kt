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

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.ui.text.input.ImeAction
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import kotlinx.coroutines.launch
import androidx.compose.ui.Alignment
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogDescription
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.network.ProviderCredential
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.windowSize
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminSection
import bot.nomnomz.dashboard.feature.admin.state.AdminSort
import androidx.compose.foundation.layout.RowScope
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_nav_admin
import nomnomzbot.composeapp.generated.resources.admin_tab_iam
import nomnomzbot.composeapp.generated.resources.admin_tab_tenants
import nomnomzbot.composeapp.generated.resources.admin_tab_audit
import nomnomzbot.composeapp.generated.resources.admin_tab_spam_defaults
import nomnomzbot.composeapp.generated.resources.admin_live_indicator
import nomnomzbot.composeapp.generated.resources.admin_live_offline
import nomnomzbot.composeapp.generated.resources.admin_registry_title
import nomnomzbot.composeapp.generated.resources.admin_registry_empty
import nomnomzbot.composeapp.generated.resources.admin_registry_live
import nomnomzbot.composeapp.generated.resources.admin_registry_offline
import nomnomzbot.composeapp.generated.resources.admin_log_title
import nomnomzbot.composeapp.generated.resources.admin_log_empty
import nomnomzbot.composeapp.generated.resources.admin_health_overall
import nomnomzbot.composeapp.generated.resources.admin_health_ok
import nomnomzbot.composeapp.generated.resources.admin_health_degraded
import nomnomzbot.composeapp.generated.resources.admin_health_unhealthy
import nomnomzbot.composeapp.generated.resources.admin_channel_live
import nomnomzbot.composeapp.generated.resources.admin_channel_offline
import nomnomzbot.composeapp.generated.resources.admin_channel_plan
import nomnomzbot.composeapp.generated.resources.admin_channel_row_type
import nomnomzbot.composeapp.generated.resources.admin_channel_search
import nomnomzbot.composeapp.generated.resources.admin_user_search
import nomnomzbot.composeapp.generated.resources.admin_service_row_type
import nomnomzbot.composeapp.generated.resources.admin_user_row_type
import nomnomzbot.composeapp.generated.resources.admin_event_log
import nomnomzbot.composeapp.generated.resources.admin_flag_disabled
import nomnomzbot.composeapp.generated.resources.admin_flag_enabled
import nomnomzbot.composeapp.generated.resources.admin_flag_enabled_rollout
import nomnomzbot.composeapp.generated.resources.admin_flag_override_broadcaster_id
import nomnomzbot.composeapp.generated.resources.admin_flag_override_clear
import nomnomzbot.composeapp.generated.resources.admin_flag_override_disable
import nomnomzbot.composeapp.generated.resources.admin_flag_override_enable
import nomnomzbot.composeapp.generated.resources.admin_grant_founder
import nomnomzbot.composeapp.generated.resources.admin_grant_tier
import nomnomzbot.composeapp.generated.resources.admin_health_degraded
import nomnomzbot.composeapp.generated.resources.admin_health_down
import nomnomzbot.composeapp.generated.resources.admin_health_ok
import nomnomzbot.composeapp.generated.resources.admin_impersonate
import nomnomzbot.composeapp.generated.resources.admin_invite_create
import nomnomzbot.composeapp.generated.resources.admin_invite_grants_founder
import nomnomzbot.composeapp.generated.resources.admin_invite_no_expiry
import nomnomzbot.composeapp.generated.resources.admin_invite_redemptions
import nomnomzbot.composeapp.generated.resources.admin_invite_revoke
import nomnomzbot.composeapp.generated.resources.admin_stats_active_channels
import nomnomzbot.composeapp.generated.resources.admin_stats_events_today
import nomnomzbot.composeapp.generated.resources.admin_stats_system_status
import nomnomzbot.composeapp.generated.resources.admin_stats_total_channels
import nomnomzbot.composeapp.generated.resources.admin_stats_total_users
import nomnomzbot.composeapp.generated.resources.admin_stats_uptime
import nomnomzbot.composeapp.generated.resources.admin_system_cpu
import nomnomzbot.composeapp.generated.resources.admin_system_memory
import nomnomzbot.composeapp.generated.resources.admin_system_version
import nomnomzbot.composeapp.generated.resources.admin_tab_billing
import nomnomzbot.composeapp.generated.resources.admin_tab_content
import nomnomzbot.composeapp.generated.resources.admin_tab_channels
import nomnomzbot.composeapp.generated.resources.admin_tab_flags
import nomnomzbot.composeapp.generated.resources.admin_tab_overview
import nomnomzbot.composeapp.generated.resources.admin_tab_system
import nomnomzbot.composeapp.generated.resources.admin_tab_users
import nomnomzbot.composeapp.generated.resources.admin_user_no_platform_access
import nomnomzbot.composeapp.generated.resources.admin_user_grant_access
import nomnomzbot.composeapp.generated.resources.admin_user_grant_access_desc
import nomnomzbot.composeapp.generated.resources.admin_user_manage_in_iam
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_iam_inactive
import nomnomzbot.composeapp.generated.resources.admin_iam_no_assignments
import nomnomzbot.composeapp.generated.resources.admin_iam_role
import nomnomzbot.composeapp.generated.resources.admin_page_previous
import nomnomzbot.composeapp.generated.resources.admin_page_next
import nomnomzbot.composeapp.generated.resources.admin_page_current
import nomnomzbot.composeapp.generated.resources.admin_sort_label
import nomnomzbot.composeapp.generated.resources.admin_sort_newest
import nomnomzbot.composeapp.generated.resources.admin_sort_oldest
import nomnomzbot.composeapp.generated.resources.admin_sort_name
import nomnomzbot.composeapp.generated.resources.admin_filter_live
import nomnomzbot.composeapp.generated.resources.admin_filter_offline
import nomnomzbot.composeapp.generated.resources.admin_filter_staff
import nomnomzbot.composeapp.generated.resources.admin_filter_streamers
import nomnomzbot.composeapp.generated.resources.admin_tab_providers
import nomnomzbot.composeapp.generated.resources.admin_providers_explain
import nomnomzbot.composeapp.generated.resources.admin_providers_empty
import nomnomzbot.composeapp.generated.resources.admin_providers_no_client_id
import nomnomzbot.composeapp.generated.resources.admin_providers_id_label
import nomnomzbot.composeapp.generated.resources.admin_providers_secret_label
import nomnomzbot.composeapp.generated.resources.admin_providers_source_stored
import nomnomzbot.composeapp.generated.resources.admin_providers_source_environment
import nomnomzbot.composeapp.generated.resources.admin_providers_source_unset
import nomnomzbot.composeapp.generated.resources.admin_providers_configure
import nomnomzbot.composeapp.generated.resources.admin_providers_configure_title
import nomnomzbot.composeapp.generated.resources.admin_providers_configure_desc
import nomnomzbot.composeapp.generated.resources.admin_providers_save
import nomnomzbot.composeapp.generated.resources.admin_providers_clear
import nomnomzbot.composeapp.generated.resources.admin_providers_clear_confirm
import nomnomzbot.composeapp.generated.resources.admin_user_channels
import nomnomzbot.composeapp.generated.resources.admin_user_role
import nomnomzbot.composeapp.generated.resources.admin_channel_empty
import nomnomzbot.composeapp.generated.resources.admin_user_empty
import nomnomzbot.composeapp.generated.resources.admin_system_services_empty
import nomnomzbot.composeapp.generated.resources.admin_health_empty
import nomnomzbot.composeapp.generated.resources.admin_billing_empty
import org.jetbrains.compose.resources.stringResource

@Composable
fun AdminScreen(controller: AdminController) {
    val state: AdminState by controller.state.collectAsStateWithLifecycle()
    LaunchedEffect(Unit) { controller.load() }
    // Fold the live operator-hub pushes (system heartbeat, channel registry, log) into state — no polling.
    controller.hubEvents?.let { events ->
        LaunchedEffect(events) { controller.subscribeToHub(events) }
    }

    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var selectedTab: Int by remember { mutableIntStateOf(0) }
    // Lazy-load the heavier Plane-C management slices only when their tab is first opened.
    LaunchedEffect(selectedTab) {
        when (selectedTab) {
            // The Users tab reads the IAM principal list too — it is what tells an operator whether the
            // person they are looking at has platform access at all. Without it every row would read
            // "no platform access", which is a lie told confidently.
            2 -> if (state.principals.isEmpty() && state.roles.isEmpty()) controller.loadIam()
            TAB_CONTENT -> {
                // Own-permission gating (ContentTab's ManageGate calls) reads state.principals/
                // effectivePermissions, same as the Users tab's UserPlatformAccess — load IAM alongside the
                // content list so the tab never renders every write control as denied just because the
                // permission lookup hasn't landed yet.
                if (state.principals.isEmpty() && state.roles.isEmpty()) controller.loadIam()
                if (state.contentDefinitions.isEmpty()) controller.loadContentDefinitions()
            }
            TAB_IAM -> if (state.principals.isEmpty() && state.roles.isEmpty()) controller.loadIam()
            TAB_TENANTS -> if (state.tenants.isEmpty()) controller.loadTenants()
            TAB_AUDIT -> if (state.auditEntries.isEmpty()) controller.loadAudit()
            TAB_SPAM_DEFAULTS -> if (state.spamDefaults == null) controller.loadSpamDefaults()
            TAB_PROVIDERS -> if (state.providerCredentials.isEmpty()) controller.loadProviders()
        }
    }
    val tabs: List<String> = listOf(
        stringResource(Res.string.admin_tab_overview),
        stringResource(Res.string.admin_tab_channels),
        stringResource(Res.string.admin_tab_users),
        stringResource(Res.string.admin_tab_system),
        stringResource(Res.string.admin_tab_flags),
        stringResource(Res.string.admin_tab_billing),
        stringResource(Res.string.admin_tab_content),
        stringResource(Res.string.admin_tab_iam),
        stringResource(Res.string.admin_tab_tenants),
        stringResource(Res.string.admin_tab_audit),
        stringResource(Res.string.admin_tab_spam_defaults),
        stringResource(Res.string.admin_tab_providers),
    )

    Column(modifier = Modifier.fillMaxSize().background(tokens.background)) {
        PageHeader(
            title = stringResource(Res.string.shell_nav_admin),
            modifier = Modifier.padding(horizontal = spacing.s6, vertical = spacing.s4),
        )
        TabsList(modifier = Modifier.padding(horizontal = spacing.s6)) {
            tabs.forEachIndexed { index, label ->
                TabsTrigger(
                    selected = selectedTab == index,
                    onClick = { selectedTab = index },
                ) {
                    Text(text = label, style = typography.sm)
                }
            }
        }

        // The top-level load error (stats/channels/users/system) was set on AdminState.error by
        // AdminController.load() but never rendered anywhere — a failed initial load left the panel silently
        // empty with no indication anything had gone wrong. Render it here so it is visible under every tab.
        AdminLoadErrorBanner(error = state.error)

        // Per-tab loading: only the tab whose data is (re)fetching shows a spinner — the tab bar and every
        // other tab stay interactive. TAB_IAM/TAB_TENANTS/TAB_AUDIT already render their own loading flag
        // (iamLoading/tenantsLoading/auditLoading) inside their own composables.
        when (selectedTab) {
            0 -> TabContentOrSpinner(isLoading = AdminSection.Overview in state.loadingSections, tokens = tokens) {
                OverviewTab(state = state)
            }
            1 -> TabContentOrSpinner(isLoading = AdminSection.Channels in state.loadingSections, tokens = tokens) {
                ChannelsTab(state = state, controller = controller)
            }
            2 -> TabContentOrSpinner(isLoading = AdminSection.Users in state.loadingSections, tokens = tokens) {
                UsersTab(state = state, controller = controller, onOpenIam = { selectedTab = TAB_IAM })
            }
            3 -> TabContentOrSpinner(isLoading = AdminSection.System in state.loadingSections, tokens = tokens) {
                SystemTab(state = state)
            }
            4 -> TabContentOrSpinner(isLoading = AdminSection.FeatureFlags in state.loadingSections, tokens = tokens) {
                FeatureFlagsTab(state = state, controller = controller)
            }
            5 -> TabContentOrSpinner(isLoading = AdminSection.Billing in state.loadingSections, tokens = tokens) {
                BillingTab(state = state, controller = controller)
            }
            TAB_CONTENT -> ContentTab(state = state, controller = controller, currentUserId = controller.currentUserId)
            TAB_IAM -> IamTab(state = state, controller = controller)
            TAB_TENANTS -> TenantsTab(state = state, controller = controller)
            TAB_AUDIT -> AuditTab(state = state, controller = controller)
            TAB_SPAM_DEFAULTS -> SpamDefaultsTab(state = state, controller = controller)
            TAB_PROVIDERS -> ProvidersTab(state = state, controller = controller)
        }
    }
}

private const val TAB_CONTENT: Int = 6
private const val TAB_IAM: Int = 7
private const val TAB_TENANTS: Int = 8
private const val TAB_AUDIT: Int = 9
private const val TAB_SPAM_DEFAULTS: Int = 10
private const val TAB_PROVIDERS: Int = 11

/** Renders [content] normally, or a centered [Spinner] in its place while [isLoading] — scoped to the current
 * tab's content area only, so a sibling tab's fetch never blocks this one. */
@Composable
private fun TabContentOrSpinner(isLoading: Boolean, tokens: Tokens, content: @Composable () -> Unit) {
    if (isLoading) {
        Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            Spinner(color = tokens.primary)
        }
    } else {
        content()
    }
}

/**
 * Renders [error] as a destructive banner when set, nothing otherwise. Extracted from [AdminScreen] so it can be
 * mounted directly in a Compose UI test without constructing a full [AdminController] — see AdminScreenTest.
 */
@Composable
internal fun AdminLoadErrorBanner(error: String?) {
    val spacing = LocalSpacing.current
    error?.let {
        ActionErrorBanner(message = it, modifier = Modifier.padding(horizontal = spacing.s6, vertical = spacing.s2))
    }
}

/**
 * A small pill reflecting the AdminHub connection: filled when the live heartbeat is flowing, muted-outline
 * "reconnecting" otherwise. It reads the truthful [AdminState.hubLive] flag the hub subscription sets — it is
 * not decorative.
 */
@Composable
private fun LiveIndicator(hubLive: Boolean) {
    val typography = LocalTypography.current
    Badge(variant = if (hubLive) BadgeVariant.Default else BadgeVariant.Outline) {
        Text(
            text = if (hubLive) stringResource(Res.string.admin_live_indicator)
            else stringResource(Res.string.admin_live_offline),
            style = typography.xs,
        )
    }
}

@Composable
internal fun OverviewTab(state: AdminState) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(text = stringResource(Res.string.admin_tab_overview), style = typography.base, color = tokens.foreground)
            LiveIndicator(hubLive = state.hubLive)
        }

        state.stats?.let { stats ->
            StatCard(label = stringResource(Res.string.admin_stats_total_channels), value = stats.totalChannels.toString())
            StatCard(label = stringResource(Res.string.admin_stats_active_channels), value = stats.activeChannels.toString())
            StatCard(label = stringResource(Res.string.admin_stats_total_users), value = stats.totalUsers.toString())
            StatCard(label = stringResource(Res.string.admin_stats_system_status), value = stats.systemStatus)
            StatCard(label = stringResource(Res.string.admin_stats_uptime), value = formatUptime(stats.botUptimeSeconds))
            StatCard(label = stringResource(Res.string.admin_stats_events_today), value = stats.eventsProcessedToday.toString())
        }

        if (state.events.isNotEmpty()) {
            Spacer(modifier = Modifier.height(spacing.s2))
            Text(
                text = stringResource(Res.string.admin_event_log),
                style = typography.base,
                color = tokens.foreground,
            )
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.events.forEachIndexed { index, event ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = spacing.s4, vertical = spacing.s3),
                            horizontalArrangement = Arrangement.SpaceBetween,
                        ) {
                            Text(text = event.message, style = typography.sm, color = tokens.cardForeground, modifier = Modifier.weight(1f))
                            Spacer(modifier = Modifier.width(spacing.s2))
                            Text(text = event.time, style = typography.xs, color = tokens.mutedForeground)
                        }
                        if (index < state.events.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }

        // Live channel registry (AdminHub go-live/offline + suspension pushes).
        Spacer(modifier = Modifier.height(spacing.s2))
        Text(text = stringResource(Res.string.admin_registry_title), style = typography.base, color = tokens.foreground)
        if (state.registry.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_registry_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.registry.forEachIndexed { index, entry ->
                        Row(
                            modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Text(
                                text = entry.channelName ?: entry.broadcasterId,
                                style = typography.sm,
                                color = tokens.cardForeground,
                                modifier = Modifier.weight(1f),
                            )
                            val label: String = entry.status
                                ?: if (entry.isLive == true) stringResource(Res.string.admin_registry_live)
                                else stringResource(Res.string.admin_registry_offline)
                            Text(
                                text = label,
                                style = typography.xs,
                                color = if (entry.isLive == true) tokens.primary else tokens.mutedForeground,
                            )
                        }
                        if (index < state.registry.lastIndex) Separator()
                    }
                }
            }
        }

        // Live operator log (AdminHub log pushes — tenant suspensions etc.).
        Spacer(modifier = Modifier.height(spacing.s2))
        Text(text = stringResource(Res.string.admin_log_title), style = typography.base, color = tokens.foreground)
        if (state.logs.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_log_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.logs.forEachIndexed { index, log ->
                        Text(
                            text = log.message,
                            style = typography.sm,
                            color = when (log.type.lowercase()) {
                                "warning" -> tokens.accent
                                "error" -> tokens.destructive
                                "success" -> tokens.primary
                                else -> tokens.cardForeground
                            },
                            modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
                        )
                        if (index < state.logs.lastIndex) Separator()
                    }
                }
            }
        }
    }
}

@Composable
internal fun ChannelsTab(state: AdminState, controller: AdminController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    var searchText: String by remember { mutableStateOf(state.channelSearch) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        AppTextField(
            value = searchText,
            onValueChange = { searchText = it },
            label = stringResource(Res.string.admin_channel_search),
            modifier = Modifier.fillMaxWidth(),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { scope.launch { controller.loadChannels(search = searchText) } }),
        )

        ListControls(
            sort = state.channelSort,
            onSort = { key -> scope.launch { controller.loadChannels(sort = key) } },
        ) {
            FilterChip(
                label = stringResource(Res.string.admin_filter_live),
                selected = state.channelLiveFilter == true,
                onClick = {
                    scope.launch {
                        if (state.channelLiveFilter == true) controller.loadChannels(clearLiveFilter = true)
                        else controller.loadChannels(isLive = true)
                    }
                },
            )
            FilterChip(
                label = stringResource(Res.string.admin_filter_offline),
                selected = state.channelLiveFilter == false,
                onClick = {
                    scope.launch {
                        if (state.channelLiveFilter == false) controller.loadChannels(clearLiveFilter = true)
                        else controller.loadChannels(isLive = false)
                    }
                },
            )
        }

        if (state.channels.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_channel_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.channels.forEachIndexed { index, channel ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = spacing.s4, vertical = spacing.s3),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                                val channelDisplayName: String =
                                    resolveRowLabel(
                                        primary = channel.displayName,
                                        secondary = channel.login,
                                        typeLabel = stringResource(Res.string.admin_channel_row_type),
                                        discriminatorSource = channel.id,
                                    )
                                Text(text = channelDisplayName, style = typography.sm, color = tokens.cardForeground)
                                Text(
                                    text = stringResource(Res.string.admin_channel_plan, channel.plan),
                                    style = typography.xs,
                                    color = tokens.mutedForeground,
                                )
                            }
                            Text(
                                text = if (channel.isLive) stringResource(Res.string.admin_channel_live)
                                else stringResource(Res.string.admin_channel_offline),
                                style = typography.xs,
                                color = if (channel.isLive) tokens.primary else tokens.mutedForeground,
                            )
                        }
                        if (index < state.channels.lastIndex) {
                            Separator()
                        }
                    }
                }
            }

            Pager(
                page = state.channelPage,
                hasMore = state.channelHasMore,
                onPage = { page -> scope.launch { controller.loadChannels(page = page) } },
            )
        }
    }
}

/**
 * Platform users, and what platform access each of them actually has.
 *
 * The list used to be read-only: id, login, role, channel count, and nothing an operator could do or
 * even learn from it. Whether a person holds platform access lives in IAM, keyed by principal, so
 * answering "can this user do anything here?" meant leaving the tab and hunting a second list.
 *
 * Each row now states that answer and offers exactly one action for it. The IAM tab keeps ownership of
 * the full principal editor — duplicating it here is how two surfaces drift into disagreeing about who
 * can do what.
 */
@Composable
internal fun UsersTab(state: AdminState, controller: AdminController, onOpenIam: () -> Unit = {}) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    var searchText: String by remember { mutableStateOf(state.userSearch) }
    var grantTarget: AdminUser? by remember { mutableStateOf(null) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        AppTextField(
            value = searchText,
            onValueChange = { searchText = it },
            label = stringResource(Res.string.admin_user_search),
            modifier = Modifier.fillMaxWidth(),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { scope.launch { controller.loadUsers(search = searchText) } }),
        )

        ListControls(
            sort = state.userSort,
            onSort = { key -> scope.launch { controller.loadUsers(sort = key) } },
        ) {
            FilterChip(
                label = stringResource(Res.string.admin_filter_staff),
                selected = state.userRoleFilter == "admin",
                onClick = {
                    scope.launch {
                        if (state.userRoleFilter == "admin") controller.loadUsers(clearRoleFilter = true)
                        else controller.loadUsers(role = "admin")
                    }
                },
            )
            FilterChip(
                label = stringResource(Res.string.admin_filter_streamers),
                selected = state.userRoleFilter == "user",
                onClick = {
                    scope.launch {
                        if (state.userRoleFilter == "user") controller.loadUsers(clearRoleFilter = true)
                        else controller.loadUsers(role = "user")
                    }
                },
            )
        }

        if (state.users.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_user_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.users.forEachIndexed { index, user ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = spacing.s4, vertical = spacing.s3),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                                val userDisplayName: String =
                                    resolveRowLabel(
                                        primary = user.displayName,
                                        secondary = user.login,
                                        typeLabel = stringResource(Res.string.admin_user_row_type),
                                        discriminatorSource = user.id,
                                    )
                                Text(text = userDisplayName, style = typography.sm, color = tokens.cardForeground)
                                Text(
                                    text = stringResource(Res.string.admin_user_role, user.role),
                                    style = typography.xs,
                                    color = tokens.mutedForeground,
                                )
                            }
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
                            ) {
                                // Act-as is offered from the Tenants tab (a support session is scoped to a tenant, not
                                // a bare user id) — see TenantDetailDrawer's "Impersonate owner" in AdminTenantsTab.kt.
                                Text(
                                    text = stringResource(Res.string.admin_user_channels, user.channelCount),
                                    style = typography.xs,
                                    color = tokens.mutedForeground,
                                )

                                UserPlatformAccess(
                                    principal = state.principals.firstOrNull { it.userId == user.id },
                                    onGrant = { grantTarget = user },
                                    onOpenIam = onOpenIam,
                                )
                            }
                        }
                        if (index < state.users.lastIndex) {
                            Separator()
                        }
                    }
                }
            }

            Pager(
                page = state.userPage,
                hasMore = state.userHasMore,
                onPage = { page -> scope.launch { controller.loadUsers(page = page) } },
            )
        }
    }

    grantTarget?.let { user ->
        GrantPlatformAccessDialog(
            user = user,
            roles = state.roles,
            onDismiss = { grantTarget = null },
            onGrant = { roleId ->
                grantTarget = null
                scope.launch {
                    controller.promoteUser(
                        userId = user.id,
                        displayName = user.displayName.ifBlank { user.login },
                        roleIds = listOf(roleId),
                    )
                }
            },
        )
    }
}

/**
 * One user's platform-access state, and the single action that follows from it.
 *
 * No principal means no platform access, and the action is to grant it. A principal means IAM owns the
 * detail, and the action is to go there — deliberately NOT a second copy of the principal editor. An
 * inactive principal is called out in destructive terms because "holds a role" and "can currently use
 * it" are different facts, and showing only the first would be the confident kind of wrong.
 */
@Composable
private fun UserPlatformAccess(
    principal: IamPrincipalSummary?,
    onGrant: () -> Unit,
    onOpenIam: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        if (principal == null) {
            Badge(variant = BadgeVariant.Outline) {
                Text(text = stringResource(Res.string.admin_user_no_platform_access), style = typography.xs)
            }
            Button(onClick = onGrant, variant = ButtonVariant.Outline, size = ButtonSize.Sm) {
                Text(text = stringResource(Res.string.admin_user_grant_access), style = typography.xs)
            }
        } else {
            if (!principal.isActive) {
                Badge(variant = BadgeVariant.Destructive) {
                    Text(text = stringResource(Res.string.admin_iam_inactive), style = typography.xs)
                }
            }
            if (principal.activeAssignments.isEmpty()) {
                Badge(variant = BadgeVariant.Outline) {
                    Text(text = stringResource(Res.string.admin_iam_no_assignments), style = typography.xs)
                }
            } else {
                principal.activeAssignments.forEach { assignment ->
                    Badge(variant = BadgeVariant.Secondary) {
                        Text(text = assignment.roleName, style = typography.xs)
                    }
                }
            }
            Button(onClick = onOpenIam, variant = ButtonVariant.Ghost, size = ButtonSize.Sm) {
                Text(text = stringResource(Res.string.admin_user_manage_in_iam), style = typography.xs)
            }
        }
    }
}

/**
 * Grants a user platform access by making them an IAM principal with one role — the same
 * createPrincipal call the IAM tab's Promote dialog makes, reached from the person rather than from the
 * principal list. Save stays disabled until a role is chosen: an access grant with no role is a
 * principal that can do nothing, which reads as a broken grant rather than a deliberate one.
 */
@Composable
private fun GrantPlatformAccessDialog(
    user: AdminUser,
    roles: List<IamRole>,
    onDismiss: () -> Unit,
    onGrant: (roleId: String) -> Unit,
) {
    var selectedRoleId: String by remember { mutableStateOf("") }
    var selectedRoleName: String by remember { mutableStateOf("") }

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_user_grant_access))
        DialogDescription(
            text =
                stringResource(
                    Res.string.admin_user_grant_access_desc,
                    user.displayName.ifBlank { user.login },
                )
        )

        PickerField(
            label = stringResource(Res.string.admin_iam_role),
            selectedLabel = selectedRoleName,
            options = roles.map { it.id to it.name },
            onSelect = { id, label ->
                selectedRoleId = id
                selectedRoleName = label
            },
        )

        DialogFooter {
            Button(onClick = onDismiss, variant = ButtonVariant.Ghost) {
                Text(text = stringResource(Res.string.admin_cancel))
            }
            Button(onClick = { onGrant(selectedRoleId) }, enabled = selectedRoleId.isNotBlank()) {
                Text(text = stringResource(Res.string.admin_user_grant_access))
            }
        }
    }
}

/**
 * Page controls for a server-paged admin list.
 *
 * The lists have always been paged server-side at 25 rows and the client always asked for page 1, so
 * row 26 onward was unreachable from the dashboard — an operator on a platform with more than 25
 * channels was quietly looking at a truncated list with nothing saying so.
 *
 * Next is driven by the server's own [hasMore], never by guessing from the row count: a page that
 * happens to hold exactly 25 rows is not evidence that a 26th exists. Both controls are outline
 * weight — navigation is not the primary act on any of these screens.
 */
/**
 * The sort selector and filter chips that sit above an admin list.
 *
 * Sort and filters are the same act — deciding which rows you are looking at, and in what order — so they
 * share one row rather than being scattered. Everything here is neutral weight: narrowing a list is
 * navigation, and spending the accent on it would compete with whatever the operator is on this page to do.
 */
@Composable
private fun ListControls(
    sort: String,
    onSort: (String) -> Unit,
    filters: @Composable RowScope.() -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = stringResource(Res.string.admin_sort_label),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        SortChip(label = stringResource(Res.string.admin_sort_newest), key = AdminSort.Newest, current = sort, onSort = onSort)
        SortChip(label = stringResource(Res.string.admin_sort_oldest), key = AdminSort.Oldest, current = sort, onSort = onSort)
        SortChip(label = stringResource(Res.string.admin_sort_name), key = AdminSort.Name, current = sort, onSort = onSort)
        Spacer(modifier = Modifier.weight(1f))
        filters()
    }
}

/** One ordering choice. The selected one is filled so the current order is readable at a glance. */
@Composable
private fun SortChip(label: String, key: String, current: String, onSort: (String) -> Unit) {
    val typography = LocalTypography.current
    Button(
        onClick = { onSort(key) },
        variant = if (current == key) ButtonVariant.Secondary else ButtonVariant.Ghost,
        size = ButtonSize.Sm,
    ) {
        Text(text = label, style = typography.xs)
    }
}

/**
 * One filter, on or off. Clicking the active one clears it — a filter an operator cannot switch off without
 * hunting for a reset control is a trap, and these lists are exactly where someone gets stuck seeing a
 * subset and believing it is everything.
 */
@Composable
private fun FilterChip(label: String, selected: Boolean, onClick: () -> Unit) {
    val typography = LocalTypography.current
    Button(
        onClick = onClick,
        variant = if (selected) ButtonVariant.Secondary else ButtonVariant.Outline,
        size = ButtonSize.Sm,
    ) {
        Text(text = label, style = typography.xs)
    }
}

@Composable
private fun Pager(page: Int, hasMore: Boolean, onPage: (Int) -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    // Page 1 with nothing after it IS the whole list; a pager there is furniture that says nothing.
    if (page <= 1 && !hasMore) return

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Button(
            onClick = { onPage(page - 1) },
            enabled = page > 1,
            variant = ButtonVariant.Outline,
            size = ButtonSize.Sm,
        ) {
            Text(text = stringResource(Res.string.admin_page_previous), style = typography.xs)
        }
        Text(
            text = stringResource(Res.string.admin_page_current, page),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        Button(
            onClick = { onPage(page + 1) },
            enabled = hasMore,
            variant = ButtonVariant.Outline,
            size = ButtonSize.Sm,
        ) {
            Text(text = stringResource(Res.string.admin_page_next), style = typography.xs)
        }
    }
}

/**
 * The platform's OAuth app credentials — what is configured for each provider, and where the value in play
 * actually comes from.
 *
 * The setup wizard writes these once and then has no further say; before this, reading back what was
 * configured or rotating a leaked secret meant editing the database by hand.
 *
 * <p>The SOURCE badge is the point of the screen, not decoration. A stored value shadows the environment, so
 * an operator who corrects a rotated secret in their .env and keeps getting 401s is looking at a stale
 * stored value they had no way to see. "Clear stored" is the way back, and it is a separate, destructive
 * action so it can never happen by saving a half-filled form.</p>
 */
@Composable
internal fun ProvidersTab(state: AdminState, controller: AdminController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    var editing: ProviderCredential? by remember { mutableStateOf(null) }
    var actionError: String? by remember { mutableStateOf(null) }
    var confirmClear: ProviderCredential? by remember { mutableStateOf(null) }

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.admin_providers_explain),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        state.providersError?.let { ActionErrorBanner(message = it) }
        actionError?.let { ActionErrorBanner(message = it) }

        if (state.providerCredentials.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_providers_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.providerCredentials.forEachIndexed { index, provider ->
                        ProviderRow(
                            provider = provider,
                            onConfigure = { editing = provider },
                            onClear = { confirmClear = provider },
                        )
                        if (index < state.providerCredentials.lastIndex) Separator()
                    }
                }
            }
        }
    }

    editing?.let { provider ->
        ProviderCredentialDialog(
            provider = provider,
            onDismiss = { editing = null },
            onSave = { clientId, clientSecret ->
                editing = null
                scope.launch {
                    actionError =
                        controller.saveProviderCredential(provider.provider, clientId, clientSecret)
                }
            },
        )
    }

    confirmClear?.let { provider ->
        ConfirmDialog(
            title = stringResource(Res.string.admin_providers_clear),
            // The counted blast radius, from the row itself: how many stored values disappear, and what
            // resolves afterwards. "Are you sure?" without the count is the thing S-CONSEQ forbids.
            message =
                stringResource(
                    Res.string.admin_providers_clear_confirm,
                    provider.provider,
                    listOf(provider.clientIdSource, provider.secretSource).count { it == "stored" },
                ),
            confirmLabel = stringResource(Res.string.admin_providers_clear),
            dismissLabel = stringResource(Res.string.admin_cancel),
            destructive = true,
            onConfirm = {
                confirmClear = null
                scope.launch { actionError = controller.clearProviderCredential(provider.provider) }
            },
            onDismiss = { confirmClear = null },
        )
    }
}

/**
 * One provider. The client id is shown because it is public; the secret is only ever a status, because no
 * read path exists that could return it.
 */
@Composable
private fun ProviderRow(
    provider: ProviderCredential,
    onConfigure: () -> Unit,
    onClear: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(text = provider.provider, style = typography.sm, color = tokens.cardForeground)
            Text(
                text = provider.clientId ?: stringResource(Res.string.admin_providers_no_client_id),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }

        SourceBadge(
            label = stringResource(Res.string.admin_providers_id_label),
            source = provider.clientIdSource,
        )
        SourceBadge(
            label = stringResource(Res.string.admin_providers_secret_label),
            source = provider.secretSource,
        )

        Button(onClick = onConfigure, variant = ButtonVariant.Outline, size = ButtonSize.Sm) {
            Text(text = stringResource(Res.string.admin_providers_configure), style = typography.xs)
        }
        if (provider.clientIdSource == "stored" || provider.secretSource == "stored") {
            Button(onClick = onClear, variant = ButtonVariant.DestructiveGhost, size = ButtonSize.Sm) {
                Text(text = stringResource(Res.string.admin_providers_clear), style = typography.xs)
            }
        }
    }
}

/** Which source the value in play came from — filled when it is a stored one, because that is the one that
 * shadows everything else and the one an operator cannot otherwise see. */
@Composable
private fun SourceBadge(label: String, source: String) {
    val typography = LocalTypography.current
    val text = when (source) {
        "stored" -> stringResource(Res.string.admin_providers_source_stored)
        "environment" -> stringResource(Res.string.admin_providers_source_environment)
        else -> stringResource(Res.string.admin_providers_source_unset)
    }
    Badge(
        variant = when (source) {
            "stored" -> BadgeVariant.Secondary
            "environment" -> BadgeVariant.Outline
            else -> BadgeVariant.Outline
        }
    ) {
        Text(text = "$label: $text", style = typography.xs)
    }
}

/**
 * Sets a client id and/or secret. Both fields start EMPTY, including the id: a pre-filled id invites a save
 * that rewrites a value the operator never meant to touch, and blank here means "leave it".
 */
@Composable
private fun ProviderCredentialDialog(
    provider: ProviderCredential,
    onDismiss: () -> Unit,
    onSave: (clientId: String, clientSecret: String) -> Unit,
) {
    var clientId: String by remember { mutableStateOf("") }
    var clientSecret: String by remember { mutableStateOf("") }

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_providers_configure_title, provider.provider))
        DialogDescription(text = stringResource(Res.string.admin_providers_configure_desc))

        AppTextField(
            value = clientId,
            onValueChange = { clientId = it },
            label = stringResource(Res.string.admin_providers_id_label),
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = clientSecret,
            onValueChange = { clientSecret = it },
            label = stringResource(Res.string.admin_providers_secret_label),
            modifier = Modifier.fillMaxWidth(),
        )

        DialogFooter {
            Button(onClick = onDismiss, variant = ButtonVariant.Ghost) {
                Text(text = stringResource(Res.string.admin_cancel))
            }
            Button(
                onClick = { onSave(clientId, clientSecret) },
                enabled = clientId.isNotBlank() || clientSecret.isNotBlank(),
            ) {
                Text(text = stringResource(Res.string.admin_providers_save))
            }
        }
    }
}

@Composable
internal fun SystemTab(state: AdminState) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        state.system?.let { sys ->
            // Truthful overall verdict — degraded/unhealthy keep their own colour, never restyled green.
            Row(
                modifier = Modifier.fillMaxWidth().background(tokens.card).padding(spacing.s3),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(text = stringResource(Res.string.admin_health_overall), style = typography.sm, color = tokens.mutedForeground)
                Badge(
                    variant = when (sys.overall.lowercase()) {
                        "healthy", "ok" -> BadgeVariant.Default
                        "degraded" -> BadgeVariant.Secondary
                        else -> BadgeVariant.Destructive
                    },
                ) {
                    Text(
                        text = when (sys.overall.lowercase()) {
                            "healthy", "ok" -> stringResource(Res.string.admin_health_ok)
                            "degraded" -> stringResource(Res.string.admin_health_degraded)
                            else -> stringResource(Res.string.admin_health_unhealthy)
                        },
                        style = typography.xs,
                    )
                }
            }
            StatCard(label = stringResource(Res.string.admin_system_version), value = sys.botVersion)
            StatCard(label = stringResource(Res.string.admin_system_memory), value = sys.memoryUsageMb.toString())
            StatCard(label = stringResource(Res.string.admin_system_cpu), value = "${(sys.cpuPercent * 10).toLong().let { t -> "${t / 10}.${t % 10}" }}%")

            Spacer(modifier = Modifier.height(spacing.s2))
            if (sys.services.isEmpty()) {
                EmptyLine(stringResource(Res.string.admin_system_services_empty))
            } else {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        sys.services.forEachIndexed { index, svc ->
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = spacing.s4, vertical = spacing.s3),
                                horizontalArrangement = Arrangement.SpaceBetween,
                            ) {
                                val svcDisplayName: String =
                                    resolveRowLabel(
                                        primary = svc.name,
                                        secondary = svc.status,
                                        typeLabel = stringResource(Res.string.admin_service_row_type),
                                        discriminatorSource = svc.status,
                                    )
                                Text(text = svcDisplayName, style = typography.sm, color = tokens.cardForeground)
                                Text(
                                    text = when (svc.status.lowercase()) {
                                        "ok", "healthy" -> stringResource(Res.string.admin_health_ok)
                                        "degraded" -> stringResource(Res.string.admin_health_degraded)
                                        else -> stringResource(Res.string.admin_health_down)
                                    },
                                    style = typography.sm,
                                    color = when (svc.status.lowercase()) {
                                        "ok", "healthy" -> tokens.primary
                                        "degraded" -> tokens.accent
                                        else -> tokens.destructive
                                    },
                                )
                            }
                            if (index < sys.services.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }

        if (state.health.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_health_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.health.forEachIndexed { index, svc ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = spacing.s4, vertical = spacing.s3),
                            horizontalArrangement = Arrangement.SpaceBetween,
                        ) {
                            val healthSvcDisplayName: String =
                                resolveRowLabel(
                                    primary = svc.name,
                                    secondary = svc.status,
                                    typeLabel = stringResource(Res.string.admin_service_row_type),
                                    discriminatorSource = svc.status,
                                )
                            Text(text = healthSvcDisplayName, style = typography.sm, color = tokens.cardForeground)
                            Text(
                                text = when (svc.status.lowercase()) {
                                    "ok", "healthy" -> stringResource(Res.string.admin_health_ok)
                                    "degraded" -> stringResource(Res.string.admin_health_degraded)
                                    else -> stringResource(Res.string.admin_health_down)
                                },
                                style = typography.sm,
                                color = when (svc.status.lowercase()) {
                                    "ok", "healthy" -> tokens.primary
                                    "degraded" -> tokens.accent
                                    else -> tokens.destructive
                                },
                            )
                        }
                        if (index < state.health.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }
    }
}

/**
 * The Plane-C feature-flag console: a global on/off [Switch] per flag (calls
 * [AdminController.setFeatureFlag], preserving the flag's existing rollout percentage / tier / consent /
 * deployment-mode gates) plus a per-tenant override row (broadcaster id + enable/disable/clear, calling
 * [AdminController.setFeatureFlagOverride] / [AdminController.deleteFeatureFlagOverride]). The row disables
 * itself while its own call is in flight so a double-tap can't race two writes for the same flag.
 */
@Composable
internal fun FeatureFlagsTab(state: AdminState, controller: AdminController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    var pendingFlagKey: String? by remember { mutableStateOf(null) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        state.actionError?.let { ActionErrorBanner(message = it) }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column {
                state.featureFlags.forEachIndexed { index, flag ->
                    val rowBusy: Boolean = pendingFlagKey == flag.key
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = spacing.s4, vertical = spacing.s3),
                        verticalArrangement = Arrangement.spacedBy(spacing.s2),
                    ) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(text = flag.key, style = typography.sm, color = tokens.cardForeground)
                                flag.description?.takeIf { it.isNotBlank() }?.let { description ->
                                    Text(text = description, style = typography.xs, color = tokens.mutedForeground)
                                }
                                Text(
                                    text =
                                        if (flag.isEnabledGlobally) {
                                            stringResource(Res.string.admin_flag_enabled_rollout, flag.rolloutPercentage)
                                        } else {
                                            stringResource(Res.string.admin_flag_disabled)
                                        },
                                    style = typography.xs,
                                    color = if (flag.isEnabledGlobally) tokens.primary else tokens.mutedForeground,
                                )
                            }
                            if (rowBusy) {
                                Spinner(color = tokens.primary)
                            } else {
                                Switch(
                                    checked = flag.isEnabledGlobally,
                                    onCheckedChange = { checked ->
                                        pendingFlagKey = flag.key
                                        scope.launch {
                                            controller.setFeatureFlag(
                                                AdminSetFeatureFlagRequest(
                                                    key = flag.key,
                                                    description = flag.description,
                                                    isEnabledGlobally = checked,
                                                    rolloutPercentage = flag.rolloutPercentage,
                                                    minTierKey = flag.minTierKey,
                                                    requiresConsent = flag.requiresConsent,
                                                    deploymentMode = flag.deploymentMode,
                                                ),
                                            )
                                            pendingFlagKey = null
                                        }
                                    },
                                    enabled = !rowBusy,
                                )
                            }
                        }

                        FeatureFlagOverrideRow(
                            flagKey = flag.key,
                            enabled = !rowBusy,
                            onSetOverride = { broadcasterId, isEnabled ->
                                pendingFlagKey = flag.key
                                scope.launch {
                                    controller.setFeatureFlagOverride(
                                        flag.key,
                                        broadcasterId,
                                        AdminSetFeatureFlagOverrideRequest(isEnabled = isEnabled),
                                    )
                                    pendingFlagKey = null
                                }
                            },
                            onClearOverride = { broadcasterId ->
                                pendingFlagKey = flag.key
                                scope.launch {
                                    controller.deleteFeatureFlagOverride(flag.key, broadcasterId)
                                    pendingFlagKey = null
                                }
                            },
                        )
                    }
                    if (index < state.featureFlags.lastIndex) {
                        Separator()
                    }
                }
            }
        }
    }
}

@Composable
private fun FeatureFlagOverrideRow(
    flagKey: String,
    enabled: Boolean,
    onSetOverride: (broadcasterId: String, isEnabled: Boolean) -> Unit,
    onClearOverride: (broadcasterId: String) -> Unit,
) {
    val spacing = LocalSpacing.current
    var broadcasterId: String by remember(flagKey) { mutableStateOf("") }
    val canAct: Boolean = enabled && broadcasterId.isNotBlank()

    // An id field + three actions in one fixed Row leaves the field a sliver on a Compact pane once the
    // buttons claim their space. At Compact the field takes its own full-width line and the three actions
    // wrap in a FlowRow beneath it instead of squeezing beside it.
    if (windowSize.isCompact) {
        Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
            AppTextField(
                value = broadcasterId,
                onValueChange = { broadcasterId = it },
                label = stringResource(Res.string.admin_flag_override_broadcaster_id),
                enabled = enabled,
                modifier = Modifier.fillMaxWidth(),
            )
            FlowRow(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                FeatureFlagOverrideActions(
                    canAct = canAct,
                    onEnable = { onSetOverride(broadcasterId, true) },
                    onDisable = { onSetOverride(broadcasterId, false) },
                    onClear = { onClearOverride(broadcasterId) },
                )
            }
        }
    } else {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            AppTextField(
                value = broadcasterId,
                onValueChange = { broadcasterId = it },
                label = stringResource(Res.string.admin_flag_override_broadcaster_id),
                enabled = enabled,
                modifier = Modifier.weight(1f),
            )
            FeatureFlagOverrideActions(
                canAct = canAct,
                onEnable = { onSetOverride(broadcasterId, true) },
                onDisable = { onSetOverride(broadcasterId, false) },
                onClear = { onClearOverride(broadcasterId) },
            )
        }
    }
}

// The Enable/Disable/Clear trio for [FeatureFlagOverrideRow] — a plain composable (no scope receiver) so it
// renders identically inside the Expanded Row and the Compact FlowRow.
@Composable
private fun FeatureFlagOverrideActions(
    canAct: Boolean,
    onEnable: () -> Unit,
    onDisable: () -> Unit,
    onClear: () -> Unit,
) {
    TextButton(onClick = onEnable, enabled = canAct) {
        Text(text = stringResource(Res.string.admin_flag_override_enable))
    }
    TextButton(onClick = onDisable, enabled = canAct) {
        Text(text = stringResource(Res.string.admin_flag_override_disable))
    }
    TextButton(onClick = onClear, enabled = canAct) {
        Text(text = stringResource(Res.string.admin_flag_override_clear))
    }
}

@Composable
internal fun BillingTab(state: AdminState, controller: AdminController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(Res.string.admin_invite_create),
                style = typography.base,
                color = tokens.foreground,
            )
            Button(onClick = {
                scope.launch {
                    controller.createInviteCode(
                        bot.nomnomz.dashboard.core.network.AdminCreateInviteCodeRequest(
                            maxRedemptions = 1,
                            grantsFoundersBadge = false,
                        )
                    )
                }
            }) {
                Text(text = stringResource(Res.string.admin_invite_create))
            }
        }

        if (state.inviteCodes.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_billing_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.inviteCodes.forEachIndexed { index, invite ->
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = spacing.s4, vertical = spacing.s3),
                            verticalArrangement = Arrangement.spacedBy(spacing.s1),
                        ) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Text(text = invite.code, style = typography.sm, color = tokens.cardForeground)
                                GlyphButton(
                                    icon = TrashGlyph,
                                    label = stringResource(Res.string.admin_invite_revoke),
                                    onClick = { scope.launch { controller.revokeInviteCode(invite.id) } },
                                    tint = tokens.destructive,
                                )
                            }
                            Text(
                                text = stringResource(
                                    Res.string.admin_invite_redemptions,
                                    invite.redemptionCount,
                                    invite.maxRedemptions,
                                ),
                                style = typography.xs,
                                color = tokens.mutedForeground,
                            )
                            if (invite.grantsFoundersBadge) {
                                Text(
                                    text = stringResource(Res.string.admin_invite_grants_founder),
                                    style = typography.xs,
                                    color = tokens.primary,
                                )
                            }
                            Text(
                                text = invite.expiresAt?.let { it } ?: stringResource(Res.string.admin_invite_no_expiry),
                                style = typography.xs,
                                color = tokens.mutedForeground,
                            )
                        }
                        if (index < state.inviteCodes.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun StatCard(label: String, value: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(tokens.card)
            .padding(spacing.s3),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = label, style = typography.sm, color = tokens.mutedForeground)
        Text(text = value, style = typography.sm, color = tokens.cardForeground)
    }
}

private fun formatUptime(seconds: Long): String {
    val days: Long = seconds / 86400
    val hours: Long = (seconds % 86400) / 3600
    val minutes: Long = (seconds % 3600) / 60
    return when {
        days > 0 -> "${days}d ${hours}h"
        hours > 0 -> "${hours}h ${minutes}m"
        else -> "${minutes}m"
    }
}
