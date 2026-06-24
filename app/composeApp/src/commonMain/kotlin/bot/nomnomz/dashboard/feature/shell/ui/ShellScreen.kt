// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.DrawerState
import androidx.compose.material3.DrawerValue
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.ModalDrawerSheet
import androidx.compose.material3.ModalNavigationDrawer
import androidx.compose.material3.Text
import androidx.compose.material3.rememberDrawerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.di.AppGraph
import bot.nomnomz.dashboard.feature.alerts.ui.AlertsScreen
import bot.nomnomz.dashboard.feature.analytics.ui.AnalyticsScreen
import bot.nomnomz.dashboard.feature.commands.ui.CommandsScreen
import bot.nomnomz.dashboard.feature.community.ui.CommunityScreen
import bot.nomnomz.dashboard.feature.economy.ui.EconomyScreen
import bot.nomnomz.dashboard.feature.games.ui.GamesScreen
import bot.nomnomz.dashboard.feature.home.ui.HomeScreen
import bot.nomnomz.dashboard.feature.integrations.ui.IntegrationsScreen
import bot.nomnomz.dashboard.feature.moderation.ui.ModerationScreen
import bot.nomnomz.dashboard.feature.rewards.ui.RewardsScreen
import bot.nomnomz.dashboard.feature.settings.ui.SettingsScreen
import bot.nomnomz.dashboard.feature.songrequests.ui.SongRequestsScreen
import bot.nomnomz.dashboard.feature.timers.ui.TimersScreen
import bot.nomnomz.dashboard.feature.tts.ui.TtsScreen
import bot.nomnomz.dashboard.feature.widgets.ui.WidgetsScreen
import bot.nomnomz.dashboard.feature.language.state.AppLanguage
import bot.nomnomz.dashboard.feature.language.state.LanguageController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.NavGroup
import bot.nomnomz.dashboard.feature.shell.nav.NavPage
import bot.nomnomz.dashboard.feature.shell.nav.ShellNav
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.app_name
import nomnomzbot.composeapp.generated.resources.language_label
import nomnomzbot.composeapp.generated.resources.language_system_default
import nomnomzbot.composeapp.generated.resources.shell_content_placeholder
import nomnomzbot.composeapp.generated.resources.shell_group_chat
import nomnomzbot.composeapp.generated.resources.shell_group_community
import nomnomzbot.composeapp.generated.resources.shell_group_home
import nomnomzbot.composeapp.generated.resources.shell_group_loyalty
import nomnomzbot.composeapp.generated.resources.shell_group_media
import nomnomzbot.composeapp.generated.resources.shell_group_stream
import nomnomzbot.composeapp.generated.resources.shell_nav_alerts
import nomnomzbot.composeapp.generated.resources.shell_nav_analytics
import nomnomzbot.composeapp.generated.resources.shell_nav_commands
import nomnomzbot.composeapp.generated.resources.shell_nav_community
import nomnomzbot.composeapp.generated.resources.shell_nav_dashboard
import nomnomzbot.composeapp.generated.resources.shell_nav_economy
import nomnomzbot.composeapp.generated.resources.shell_nav_games
import nomnomzbot.composeapp.generated.resources.shell_nav_integrations
import nomnomzbot.composeapp.generated.resources.shell_nav_menu_open
import nomnomzbot.composeapp.generated.resources.shell_nav_moderation
import nomnomzbot.composeapp.generated.resources.shell_nav_overlays
import nomnomzbot.composeapp.generated.resources.shell_nav_rewards
import nomnomzbot.composeapp.generated.resources.shell_nav_settings
import nomnomzbot.composeapp.generated.resources.shell_nav_song_requests
import nomnomzbot.composeapp.generated.resources.shell_nav_timers
import nomnomzbot.composeapp.generated.resources.shell_nav_tts
import nomnomzbot.composeapp.generated.resources.shell_profile_logout
import nomnomzbot.composeapp.generated.resources.shell_profile_open
import nomnomzbot.composeapp.generated.resources.shell_role_broadcaster
import nomnomzbot.composeapp.generated.resources.shell_role_editor
import nomnomzbot.composeapp.generated.resources.shell_role_moderator
import nomnomzbot.composeapp.generated.resources.shell_role_supermod
import nomnomzbot.composeapp.generated.resources.shell_topbar_channel_label
import nomnomzbot.composeapp.generated.resources.shell_topbar_hub_label
import org.jetbrains.compose.resources.stringResource

// The authenticated Main shell (frontend-ia.md §2): a persistent grouped, role-gated sidebar with a bottom
// profile block, a top bar (page title + active-channel chip + realtime dot), and the content area. The
// sidebar is rendered from the single [ShellNav] inventory filtered by the caller's [ManagementRole] — to
// move or re-gate a page you edit that one list, never this file. Only the Integrations page hosts its real
// screen today; the rest show a labelled placeholder until their slice lands.
// Below this width the persistent sidebar would crowd the content, so the shell switches to a mobile layout:
// the sidebar becomes a hamburger-toggled overlay drawer. A layout breakpoint (a window concern), not a
// design-system spacing token.
private val CompactBreakpoint: Dp = 720.dp

@Composable
fun ShellScreen(
    graph: AppGraph,
    languageController: LanguageController,
    user: SessionUser?,
    role: ManagementRole,
    onLogout: () -> Unit,
) {
    val tokens = LocalTokens.current
    var selected: ShellRoute by remember { mutableStateOf(ShellRoute.Dashboard) }

    BoxWithConstraints(modifier = Modifier.fillMaxSize().background(tokens.background)) {
        val compact: Boolean = maxWidth < CompactBreakpoint

        if (compact) {
            // Mobile: the sidebar is an overlay drawer behind a hamburger; picking a page closes it.
            val drawerState: DrawerState = rememberDrawerState(DrawerValue.Closed)
            val scope: CoroutineScope = rememberCoroutineScope()

            ModalNavigationDrawer(
                drawerState = drawerState,
                drawerContent = {
                    ModalDrawerSheet(drawerContainerColor = tokens.sidebar) {
                        Sidebar(
                            role = role,
                            selected = selected,
                            onSelect = {
                                selected = it
                                scope.launch { drawerState.close() }
                            },
                            user = user,
                            languageController = languageController,
                            onLogout = onLogout,
                        )
                    }
                },
            ) {
                Column(modifier = Modifier.fillMaxSize()) {
                    TopBar(
                        title = selected.label(),
                        channelName = user?.displayName,
                        onMenu = { scope.launch { drawerState.open() } },
                    )
                    ShellContent(selected = selected, graph = graph)
                }
            }
        } else {
            // Desktop / wide: persistent sidebar beside the content.
            Row(modifier = Modifier.fillMaxSize()) {
                Sidebar(
                    role = role,
                    selected = selected,
                    onSelect = { selected = it },
                    user = user,
                    languageController = languageController,
                    onLogout = onLogout,
                )
                Column(modifier = Modifier.fillMaxSize()) {
                    TopBar(title = selected.label(), channelName = user?.displayName, onMenu = null)
                    ShellContent(selected = selected, graph = graph)
                }
            }
        }
    }
}

@Composable
private fun ShellContent(selected: ShellRoute, graph: AppGraph) {
    when (selected) {
        ShellRoute.Dashboard -> HomeScreen(controller = graph.homeController)
        ShellRoute.Community -> CommunityScreen(controller = graph.communityController)
        ShellRoute.Commands -> CommandsScreen(controller = graph.commandsController)
        ShellRoute.Timers -> TimersScreen(controller = graph.timersController)
        ShellRoute.Moderation -> ModerationScreen(controller = graph.moderationController)
        ShellRoute.Analytics -> AnalyticsScreen(controller = graph.analyticsController)
        ShellRoute.Rewards -> RewardsScreen(controller = graph.rewardsController)
        ShellRoute.SongRequests -> SongRequestsScreen(controller = graph.songRequestsController)
        ShellRoute.Tts -> TtsScreen(controller = graph.ttsController)
        ShellRoute.Games -> GamesScreen(controller = graph.gamesController)
        ShellRoute.Integrations -> IntegrationsScreen(controller = graph.integrationsController)
        ShellRoute.Settings -> SettingsScreen(controller = graph.settingsController)
        ShellRoute.Economy -> EconomyScreen(controller = graph.economyController)
        ShellRoute.Alerts -> AlertsScreen(controller = graph.alertsController)
        ShellRoute.Widgets -> WidgetsScreen(controller = graph.widgetsController)
        else -> PagePlaceholder(title = selected.label())
    }
}

@Composable
private fun Sidebar(
    role: ManagementRole,
    selected: ShellRoute,
    onSelect: (ShellRoute) -> Unit,
    user: SessionUser?,
    languageController: LanguageController,
    onLogout: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val visible: List<NavPage> = ShellNav.visiblePagesFor(role)
    val groups: Map<NavGroup, List<NavPage>> =
        visible.filter { it.group != NavGroup.Pinned }.groupBy { it.group }
    val pinned: List<NavPage> = visible.filter { it.group == NavGroup.Pinned }

    Column(
        modifier = Modifier
            .fillMaxHeight()
            .width(spacing.s24 * 2.5f)
            .background(tokens.sidebar)
            .padding(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.app_name),
            style = typography.lg,
            color = tokens.sidebarForeground,
            modifier = Modifier.padding(start = spacing.s2, top = spacing.s2, bottom = spacing.s3),
        )

        Column(
            modifier = Modifier.weight(1f).verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            groups.forEach { (group, pages) ->
                GroupLabel(label = group.label())
                pages.forEach { page ->
                    NavItem(route = page.route, selected = page.route == selected) { onSelect(page.route) }
                }
            }
        }

        if (pinned.isNotEmpty()) {
            HorizontalDivider(
                color = tokens.sidebarBorder,
                modifier = Modifier.padding(vertical = spacing.s2),
            )
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                pinned.forEach { page ->
                    NavItem(route = page.route, selected = page.route == selected) { onSelect(page.route) }
                }
            }
        }

        HorizontalDivider(
            color = tokens.sidebarBorder,
            modifier = Modifier.padding(top = spacing.s2, bottom = spacing.s2),
        )
        ProfileBlock(
            user = user,
            role = role,
            languageController = languageController,
            onLogout = onLogout,
        )
    }
}

@Composable
private fun GroupLabel(label: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = label,
        style = typography.xs,
        color = tokens.mutedForeground,
        modifier = Modifier.padding(start = spacing.s2, top = spacing.s3, bottom = spacing.s1),
    )
}

@Composable
private fun NavItem(route: ShellRoute, selected: Boolean, onClick: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val container: Color = if (selected) tokens.sidebarAccent else Color.Transparent
    val content: Color = if (selected) tokens.sidebarAccentForeground else tokens.sidebarForeground

    Box(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(container)
            .selectable(selected = selected, role = Role.Tab, onClick = onClick)
            .padding(horizontal = spacing.s3, vertical = spacing.s2),
    ) {
        Text(text = route.label(), style = typography.sm, color = content)
    }
}

@Composable
private fun ProfileBlock(
    user: SessionUser?,
    role: ManagementRole,
    languageController: LanguageController,
    onLogout: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var open: Boolean by remember { mutableStateOf(false) }
    val menuLabel: String = stringResource(Res.string.shell_profile_open)
    val name: String = user?.displayName ?: ""
    val roleLabel: String = role.label()

    Box {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(tokens.radius.md))
                .clickable(onClick = { open = true })
                .semantics { contentDescription = menuLabel }
                .padding(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Avatar(name = name)
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = name,
                    style = typography.sm,
                    color = tokens.sidebarForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = roleLabel,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }

        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            // Identity header (non-interactive).
            Column(modifier = Modifier.padding(horizontal = spacing.s3, vertical = spacing.s2)) {
                Text(text = name, style = typography.sm, color = tokens.popoverForeground)
                Text(text = roleLabel, style = typography.xs, color = tokens.mutedForeground)
            }
            HorizontalDivider(color = tokens.border)

            // Language (app prefs, frontend-ia.md §4).
            AppLanguage.entries.forEach { language ->
                DropdownMenuItem(
                    text = { Text(text = language.menuLabel(), style = typography.sm) },
                    onClick = {
                        languageController.select(language)
                        open = false
                    },
                )
            }
            HorizontalDivider(color = tokens.border)

            // Log out — returns to the Connect/Setup gate (frontend-ia.md §4).
            DropdownMenuItem(
                text = {
                    Text(
                        text = stringResource(Res.string.shell_profile_logout),
                        style = typography.sm,
                        color = tokens.destructive,
                    )
                },
                onClick = {
                    open = false
                    onLogout()
                },
            )
        }
    }
}

@Composable
private fun Avatar(name: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val initial: String = name.trim().firstOrNull()?.uppercase() ?: "?"

    Box(
        modifier = Modifier.size(spacing.s8).clip(CircleShape).background(tokens.sidebarPrimary),
        contentAlignment = Alignment.Center,
    ) {
        Text(text = initial, style = typography.sm, color = tokens.sidebarPrimaryForeground)
    }
}

@Composable
private fun TopBar(title: String, channelName: String?, onMenu: (() -> Unit)?) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(spacing.s16)
                .background(tokens.background)
                .padding(horizontal = spacing.s6),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Row(
                modifier = Modifier.weight(1f),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                if (onMenu != null) HamburgerButton(onClick = onMenu)
                Text(
                    text = title,
                    style = typography.xl,
                    color = tokens.foreground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                if (!channelName.isNullOrBlank()) ChannelChip(name = channelName)
                HubDot()
            }
        }
        HorizontalDivider(color = tokens.border)
    }
}

@Composable
private fun HamburgerButton(onClick: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val label: String = stringResource(Res.string.shell_nav_menu_open)

    Column(
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.md))
            .clickable(onClick = onClick)
            .semantics { contentDescription = label }
            .padding(spacing.s2),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        repeat(3) {
            Box(
                modifier = Modifier
                    .width(spacing.s4)
                    .height(spacing.s0_5)
                    .clip(RoundedCornerShape(tokens.radius.sm))
                    .background(tokens.foreground),
            )
        }
    }
}

@Composable
private fun ChannelChip(name: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val description: String = stringResource(Res.string.shell_topbar_channel_label, name)

    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.muted)
            .semantics { contentDescription = description }
            .padding(horizontal = spacing.s2, vertical = spacing.s1),
    ) {
        Text(
            text = name,
            style = typography.sm,
            color = tokens.mutedForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

@Composable
private fun HubDot() {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    val description: String = stringResource(Res.string.shell_topbar_hub_label)

    Box(
        modifier = Modifier
            .size(spacing.s2)
            .clip(CircleShape)
            .background(tokens.primary)
            .semantics { contentDescription = description },
    )
}

@Composable
private fun PagePlaceholder(title: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier = Modifier.fillMaxSize().padding(spacing.s6),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(text = title, style = typography.lg, color = tokens.foreground)
            Text(
                text = stringResource(Res.string.shell_content_placeholder),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
        }
    }
}

// ── Label mappings (the single place each route/group/role/language maps to its localized string) ──────────

@Composable
private fun ShellRoute.label(): String =
    stringResource(
        when (this) {
            ShellRoute.Dashboard -> Res.string.shell_nav_dashboard
            ShellRoute.Commands -> Res.string.shell_nav_commands
            ShellRoute.Timers -> Res.string.shell_nav_timers
            ShellRoute.Moderation -> Res.string.shell_nav_moderation
            ShellRoute.Rewards -> Res.string.shell_nav_rewards
            ShellRoute.Economy -> Res.string.shell_nav_economy
            ShellRoute.Games -> Res.string.shell_nav_games
            ShellRoute.SongRequests -> Res.string.shell_nav_song_requests
            ShellRoute.Tts -> Res.string.shell_nav_tts
            ShellRoute.Widgets -> Res.string.shell_nav_overlays
            ShellRoute.Alerts -> Res.string.shell_nav_alerts
            ShellRoute.Analytics -> Res.string.shell_nav_analytics
            ShellRoute.Community -> Res.string.shell_nav_community
            ShellRoute.Integrations -> Res.string.shell_nav_integrations
            ShellRoute.Settings -> Res.string.shell_nav_settings
        }
    )

@Composable
private fun NavGroup.label(): String =
    when (this) {
        NavGroup.Home -> stringResource(Res.string.shell_group_home)
        NavGroup.Chat -> stringResource(Res.string.shell_group_chat)
        NavGroup.Loyalty -> stringResource(Res.string.shell_group_loyalty)
        NavGroup.Media -> stringResource(Res.string.shell_group_media)
        NavGroup.Stream -> stringResource(Res.string.shell_group_stream)
        NavGroup.Community -> stringResource(Res.string.shell_group_community)
        NavGroup.Pinned -> "" // pinned items render without a group header
    }

@Composable
private fun ManagementRole.label(): String =
    stringResource(
        when (this) {
            ManagementRole.Moderator -> Res.string.shell_role_moderator
            ManagementRole.SuperMod -> Res.string.shell_role_supermod
            ManagementRole.Editor -> Res.string.shell_role_editor
            ManagementRole.Broadcaster -> Res.string.shell_role_broadcaster
        }
    )

@Composable
private fun AppLanguage.menuLabel(): String =
    when (this) {
        AppLanguage.System ->
            "${stringResource(Res.string.language_label)}: ${stringResource(Res.string.language_system_default)}"
        AppLanguage.English -> "English"
        AppLanguage.Dutch -> "Nederlands"
    }
