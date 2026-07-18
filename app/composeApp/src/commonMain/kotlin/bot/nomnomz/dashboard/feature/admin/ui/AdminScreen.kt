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
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import kotlinx.coroutines.launch
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_nav_admin
import nomnomzbot.composeapp.generated.resources.admin_tab_iam
import nomnomzbot.composeapp.generated.resources.admin_tab_tenants
import nomnomzbot.composeapp.generated.resources.admin_tab_audit
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
import nomnomzbot.composeapp.generated.resources.admin_event_log
import nomnomzbot.composeapp.generated.resources.admin_flag_disabled
import nomnomzbot.composeapp.generated.resources.admin_flag_enabled
import nomnomzbot.composeapp.generated.resources.admin_grant_founder
import nomnomzbot.composeapp.generated.resources.admin_grant_tier
import nomnomzbot.composeapp.generated.resources.admin_health_degraded
import nomnomzbot.composeapp.generated.resources.admin_health_down
import nomnomzbot.composeapp.generated.resources.admin_health_ok
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
import nomnomzbot.composeapp.generated.resources.admin_tab_channels
import nomnomzbot.composeapp.generated.resources.admin_tab_flags
import nomnomzbot.composeapp.generated.resources.admin_tab_overview
import nomnomzbot.composeapp.generated.resources.admin_tab_system
import nomnomzbot.composeapp.generated.resources.admin_tab_users
import nomnomzbot.composeapp.generated.resources.admin_user_channels
import nomnomzbot.composeapp.generated.resources.admin_user_role
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
            TAB_IAM -> if (state.principals.isEmpty() && state.roles.isEmpty()) controller.loadIam()
            TAB_TENANTS -> if (state.tenants.isEmpty()) controller.loadTenants()
            TAB_AUDIT -> if (state.auditEntries.isEmpty()) controller.loadAudit()
        }
    }
    val tabs: List<String> = listOf(
        stringResource(Res.string.admin_tab_overview),
        stringResource(Res.string.admin_tab_channels),
        stringResource(Res.string.admin_tab_users),
        stringResource(Res.string.admin_tab_system),
        stringResource(Res.string.admin_tab_flags),
        stringResource(Res.string.admin_tab_billing),
        stringResource(Res.string.admin_tab_iam),
        stringResource(Res.string.admin_tab_tenants),
        stringResource(Res.string.admin_tab_audit),
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

        if (state.isLoading) {
            Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Spinner(color = tokens.primary)
            }
            return@Column
        }

        when (selectedTab) {
            0 -> OverviewTab(state = state)
            1 -> ChannelsTab(state = state)
            2 -> UsersTab(state = state)
            3 -> SystemTab(state = state)
            4 -> FeatureFlagsTab(state = state, controller = controller)
            5 -> BillingTab(state = state, controller = controller)
            TAB_IAM -> IamTab(state = state, controller = controller)
            TAB_TENANTS -> TenantsTab(state = state, controller = controller)
            TAB_AUDIT -> AuditTab(state = state, controller = controller)
        }
    }
}

private const val TAB_IAM: Int = 6
private const val TAB_TENANTS: Int = 7
private const val TAB_AUDIT: Int = 8

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
private fun OverviewTab(state: AdminState) {
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
            Text(text = stringResource(Res.string.admin_registry_empty), style = typography.sm, color = tokens.mutedForeground)
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
            Text(text = stringResource(Res.string.admin_log_empty), style = typography.sm, color = tokens.mutedForeground)
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
private fun ChannelsTab(state: AdminState) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(spacing.s4),
    ) {
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
                            Text(text = channel.displayName, style = typography.sm, color = tokens.cardForeground)
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
    }
}

@Composable
private fun UsersTab(state: AdminState) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(spacing.s4),
    ) {
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
                            Text(text = user.displayName, style = typography.sm, color = tokens.cardForeground)
                            Text(
                                text = stringResource(Res.string.admin_user_role, user.role),
                                style = typography.xs,
                                color = tokens.mutedForeground,
                            )
                        }
                        Text(
                            text = stringResource(Res.string.admin_user_channels, user.channelCount),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                    }
                    if (index < state.users.lastIndex) {
                        Separator()
                    }
                }
            }
        }
    }
}

@Composable
private fun SystemTab(state: AdminState) {
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
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    sys.services.forEachIndexed { index, svc ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = spacing.s4, vertical = spacing.s3),
                            horizontalArrangement = Arrangement.SpaceBetween,
                        ) {
                            Text(text = svc.name, style = typography.sm, color = tokens.cardForeground)
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

        Card(modifier = Modifier.fillMaxWidth()) {
            Column {
                state.health.forEachIndexed { index, svc ->
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = spacing.s4, vertical = spacing.s3),
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        Text(text = svc.name, style = typography.sm, color = tokens.cardForeground)
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

@Composable
private fun FeatureFlagsTab(state: AdminState, controller: AdminController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(spacing.s4),
    ) {
        Card(modifier = Modifier.fillMaxWidth()) {
            Column {
                state.featureFlags.forEachIndexed { index, flag ->
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = spacing.s4, vertical = spacing.s3),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text(text = flag.featureKey, style = typography.sm, color = tokens.cardForeground)
                        }
                        Text(
                            text = if (flag.isEnabled) stringResource(Res.string.admin_flag_enabled)
                            else stringResource(Res.string.admin_flag_disabled),
                            style = typography.sm,
                            color = if (flag.isEnabled) tokens.primary else tokens.mutedForeground,
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
private fun BillingTab(state: AdminState, controller: AdminController) {
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
                                imageVector = TrashGlyph,
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
