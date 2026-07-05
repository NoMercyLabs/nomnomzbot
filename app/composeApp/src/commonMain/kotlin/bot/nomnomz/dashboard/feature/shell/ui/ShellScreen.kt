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

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.EaseOut
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.expandVertically
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsHoveredAsState
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
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Sheet
import bot.nomnomz.dashboard.core.designsystem.component.SheetSide
import bot.nomnomz.dashboard.core.designsystem.icon.ChevronDownGlyph
import androidx.compose.runtime.key
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshots.SnapshotStateMap
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import coil3.compose.AsyncImage
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.filterNotNull
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.feature.shell.state.ChannelSwitcherController
import bot.nomnomz.dashboard.feature.shell.state.SwitcherState
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.di.AppGraph
import bot.nomnomz.dashboard.core.navigation.RouteStore
import bot.nomnomz.dashboard.feature.alerts.ui.AlertsScreen
import bot.nomnomz.dashboard.feature.analytics.ui.AnalyticsScreen
import bot.nomnomz.dashboard.feature.chat.ui.ChatScreen
import bot.nomnomz.dashboard.feature.commands.ui.CommandsScreen
import bot.nomnomz.dashboard.feature.community.ui.CommunityScreen
import bot.nomnomz.dashboard.feature.eventresponses.ui.EventResponsesScreen
import bot.nomnomz.dashboard.feature.discord.ui.DiscordScreen
import bot.nomnomz.dashboard.feature.economy.ui.EconomyScreen
import bot.nomnomz.dashboard.feature.games.ui.GamesScreen
import bot.nomnomz.dashboard.feature.home.ui.HomeScreen
import bot.nomnomz.dashboard.feature.integrations.ui.IntegrationsScreen
import bot.nomnomz.dashboard.feature.moderation.ui.ModerationScreen
import bot.nomnomz.dashboard.feature.music.ui.MusicScreen
import bot.nomnomz.dashboard.feature.pipelines.ui.PipelinesScreen
import bot.nomnomz.dashboard.feature.quotes.ui.QuotesScreen
import bot.nomnomz.dashboard.feature.sound.ui.SoundScreen
import bot.nomnomz.dashboard.feature.rewards.ui.RewardsScreen
import bot.nomnomz.dashboard.feature.roles.ui.RolesScreen
import bot.nomnomz.dashboard.feature.settings.ui.SettingsScreen
import bot.nomnomz.dashboard.feature.splash.ui.SplashScreen
import bot.nomnomz.dashboard.feature.songrequests.ui.SongRequestsScreen
import bot.nomnomz.dashboard.feature.timers.ui.TimersScreen
import bot.nomnomz.dashboard.feature.tts.ui.TtsScreen
import bot.nomnomz.dashboard.feature.widgets.ui.WidgetsScreen
import bot.nomnomz.dashboard.feature.features.ui.FeaturesScreen
import bot.nomnomz.dashboard.feature.webhooks.ui.WebhooksScreen
import bot.nomnomz.dashboard.feature.federation.ui.FederationScreen
import bot.nomnomz.dashboard.feature.customevents.ui.CustomEventsScreen
import bot.nomnomz.dashboard.feature.admin.ui.AdminScreen
import bot.nomnomz.dashboard.feature.codescripts.ui.CodeScriptsScreen
import bot.nomnomz.dashboard.feature.language.state.AppLanguage
import bot.nomnomz.dashboard.feature.language.state.LanguageController
import bot.nomnomz.dashboard.feature.participant.ui.ParticipantShell
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.NavGroup
import bot.nomnomz.dashboard.feature.shell.nav.NavPage
import bot.nomnomz.dashboard.feature.shell.nav.ShellNav
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.state.ShellAccess
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.app_name
import nomnomzbot.composeapp.generated.resources.language_label
import nomnomzbot.composeapp.generated.resources.language_system_default
import nomnomzbot.composeapp.generated.resources.shell_group_chat
import nomnomzbot.composeapp.generated.resources.shell_group_community
import nomnomzbot.composeapp.generated.resources.shell_group_connect
import nomnomzbot.composeapp.generated.resources.shell_group_home
import nomnomzbot.composeapp.generated.resources.shell_group_loyalty
import nomnomzbot.composeapp.generated.resources.shell_group_moderation
import nomnomzbot.composeapp.generated.resources.shell_group_music
import nomnomzbot.composeapp.generated.resources.shell_group_setup
import nomnomzbot.composeapp.generated.resources.shell_group_stream
import nomnomzbot.composeapp.generated.resources.shell_nav_alerts
import nomnomzbot.composeapp.generated.resources.shell_nav_analytics
import nomnomzbot.composeapp.generated.resources.shell_nav_chat
import nomnomzbot.composeapp.generated.resources.shell_nav_commands
import nomnomzbot.composeapp.generated.resources.shell_nav_community
import nomnomzbot.composeapp.generated.resources.shell_nav_dashboard
import nomnomzbot.composeapp.generated.resources.shell_nav_discord
import nomnomzbot.composeapp.generated.resources.shell_nav_economy
import nomnomzbot.composeapp.generated.resources.shell_nav_games
import nomnomzbot.composeapp.generated.resources.shell_nav_integrations
import nomnomzbot.composeapp.generated.resources.shell_nav_menu_open
import nomnomzbot.composeapp.generated.resources.shell_nav_moderation
import nomnomzbot.composeapp.generated.resources.shell_nav_music
import nomnomzbot.composeapp.generated.resources.shell_nav_sound
import nomnomzbot.composeapp.generated.resources.shell_nav_overlays
import nomnomzbot.composeapp.generated.resources.shell_nav_pipelines
import nomnomzbot.composeapp.generated.resources.shell_nav_quotes
import nomnomzbot.composeapp.generated.resources.shell_nav_rewards
import nomnomzbot.composeapp.generated.resources.shell_nav_roles
import nomnomzbot.composeapp.generated.resources.shell_nav_settings
import nomnomzbot.composeapp.generated.resources.shell_nav_event_responses
import nomnomzbot.composeapp.generated.resources.shell_nav_features
import nomnomzbot.composeapp.generated.resources.shell_nav_webhooks
import nomnomzbot.composeapp.generated.resources.shell_nav_admin
import nomnomzbot.composeapp.generated.resources.shell_nav_code_scripts
import nomnomzbot.composeapp.generated.resources.shell_nav_custom_events
import nomnomzbot.composeapp.generated.resources.shell_nav_federation
import nomnomzbot.composeapp.generated.resources.shell_nav_song_requests
import nomnomzbot.composeapp.generated.resources.shell_nav_timers
import nomnomzbot.composeapp.generated.resources.shell_nav_tts
import nomnomzbot.composeapp.generated.resources.shell_profile_logout
import nomnomzbot.composeapp.generated.resources.shell_profile_open
import nomnomzbot.composeapp.generated.resources.shell_reconnect_menu
import nomnomzbot.composeapp.generated.resources.shell_role_broadcaster
import nomnomzbot.composeapp.generated.resources.shell_role_editor
import nomnomzbot.composeapp.generated.resources.shell_role_moderator
import nomnomzbot.composeapp.generated.resources.shell_role_supermod
import nomnomzbot.composeapp.generated.resources.shell_role_viewer
import nomnomzbot.composeapp.generated.resources.hub_alert
import nomnomzbot.composeapp.generated.resources.shell_channel_bot_not_installed
import nomnomzbot.composeapp.generated.resources.shell_channel_pick
import nomnomzbot.composeapp.generated.resources.shell_topbar_channel_label
import nomnomzbot.composeapp.generated.resources.shell_topbar_hub_label
import org.jetbrains.compose.resources.stringResource

// The authenticated Main shell (frontend-ia.md §2): a persistent grouped, role-gated sidebar with a bottom
// profile block, a top bar (page title + active-channel chip + realtime dot), and the content area. The
// sidebar is rendered from the single [ShellNav] inventory filtered by the caller's [ManagementRole] — to
// move or re-gate a page you edit that one list, never this file. All 21 management pages are wired to
// real screens with full CRUD and role-gated write controls.
// Below this width the persistent sidebar would crowd the content, so the shell switches to a mobile layout:
// the sidebar becomes a hamburger-toggled overlay drawer. A layout breakpoint (a window concern), not a
// design-system spacing token.
private val CompactBreakpoint: Dp = 720.dp

// shadcn Collapsible animation duration (0.2s ease-out) — the sidebar accordion matches it.
private const val CollapseDurationMillis: Int = 200

@Composable
fun ShellScreen(
    graph: AppGraph,
    languageController: LanguageController,
    routeStore: RouteStore,
    user: SessionUser?,
    access: ShellAccess.Resolved,
    onLogout: () -> Unit,
) {
    // Load the channel roster once, for BOTH rungs, so the channel switcher is populated whether the caller lands
    // on the management OR the participant surface — a participant must always be able to switch channels (e.g. a
    // moderator viewing a channel they mod, or switching back to a channel they broadcast). Kept ABOVE the
    // participant early-return so it runs regardless of role; App re-resolves access on every activeChannelId change.
    LaunchedEffect(Unit) { graph.channelSwitcherController.load() }

    // Mid-switch guard (frontend-ia.md §7 — never over-grant). On a channel switch the resolved [access] still
    // describes the PREVIOUS channel until the new /effective/me probe lands; render a neutral splash for that
    // brief window rather than the old channel's (possibly higher) role. Kept ABOVE the role fork so switching
    // from a channel the caller broadcasts to one they only moderate — or can't manage at all — never flashes the
    // full sidebar. A failed/empty resolve (channelId "") or the first boot (activeChannelId null) falls through
    // to render the fail-closed surface instead of spinning forever.
    val activeChannelId: String? by
        graph.channelSwitcherController.activeChannelId.collectAsStateWithLifecycle()
    val switchingChannel: Boolean =
        activeChannelId != null && access.channelId.isNotEmpty() && access.channelId != activeChannelId
    if (switchingChannel) {
        SplashScreen()
        return
    }

    // One shell, three rungs (participant → mod → broadcaster), never forked. A caller with no Plane-B management
    // role is a PARTICIPANT (Rung 0): the same shell renders the participant surface — their own profile/standing,
    // the channel they're watching, and the read-mostly + self-service slices they're permitted — gated by their
    // Plane-A community standing, not a management role. A non-null role gets the management shell below unchanged.
    val role: ManagementRole? = access.role
    if (role == null) {
        ParticipantShell(
            graph = graph,
            languageController = languageController,
            user = user,
            access = access,
            onLogout = onLogout,
        )
        return
    }

    val tokens = LocalTokens.current

    // The routes this caller may actually OPEN on the active channel: the role's visible pages (frontend-ia.md §7)
    // plus the admin console when they're a platform admin. Drives both the initial page seed and the gate below.
    val allowedRoutes: Set<ShellRoute> =
        remember(role, user?.isAdmin) {
            val visible: Set<ShellRoute> = ShellNav.visiblePagesFor(role).map { it.route }.toSet()
            if (user?.isAdmin == true) visible + ShellRoute.Admin else visible
        }

    // [requestedRoute] is the raw navigation intent (URL-seeded, sidebar taps, browser Back/Forward); [selected] is
    // what actually renders — coerced down to Home (floors at Moderator, always reachable) whenever the intent sits
    // on a page the caller can't read. This one derivation re-gates the content on EVERY path that could move the
    // selection below the floor, including a LIVE role drop (a permission revoked over SignalR, or a switch to a
    // channel the caller manages less), so the content host can never render — or crash on — a page the sidebar is
    // hiding. Routing responds to permission changes with no reload. The route persisted to the URL is the coerced
    // one, so a reload never restores a page the caller has since lost.
    var requestedRoute: ShellRoute by remember { mutableStateOf(routeStore.initialRoute()) }
    val selected: ShellRoute = if (requestedRoute in allowedRoutes) requestedRoute else ShellRoute.Dashboard
    LaunchedEffect(selected) { routeStore.save(selected) }
    LaunchedEffect(routeStore) { routeStore.externalChanges.collect { requestedRoute = it } }
    // Keep the dashboard hub connected to the ACTIVE channel for the whole session — not as a side effect of the
    // Home page loading. It (re)connects and rejoins the channel group whenever the active channel resolves or
    // changes, so every page (the chat feed, alerts, live stats) receives realtime events on a direct load or
    // after a channel switch — previously the socket was opened only when Home loaded, so chat never updated
    // when the user landed elsewhere.
    LaunchedEffect(Unit) {
        graph.channelSwitcherController.activeChannelId.filterNotNull().collect { channelId ->
            val url: String? = graph.sessionStore.baseUrl()
            val token: String? = graph.sessionStore.accessToken()
            if (url != null && token != null) {
                graph.dashboardHubClient.connect(url, token, channelId)
            }
        }
    }
    // Surface hub signals that affect the whole shell frame regardless of the active page.
    val hubEvents = graph.dashboardHubClient.events
    LaunchedEffect(hubEvents) {
        hubEvents.collect { evt ->
            when (evt) {
                // Integration token expired / disconnected — show a frame-level error notification.
                is HubEvent.AlertTriggered -> {
                    val detail: String = evt.alert.message ?: evt.alert.type
                    graph.feedbackController.error(Res.string.hub_alert, detail)
                }
                // A permission changed for this channel — re-probe the caller's management role so
                // write gates re-evaluate immediately without requiring a page reload.
                is HubEvent.PermissionChanged -> graph.shellAccessController.load()
                else -> Unit
            }
        }
    }

    // A dead Twitch token is recovered IN PLACE — never a logout (never-logout-for-scope-or-schema-changes).
    // The profile menu's "Reconnect Twitch" AND the auto-prompt on load both run [ConnectController.reconnect] —
    // the REDIRECT re-auth for the broadcaster (device-code only on the secret-less fallback); the [ReconnectBanner]
    // below surfaces it at the top of the shell and hides on success.
    val reconnectScope: CoroutineScope = rememberCoroutineScope()
    var reconnectJob: Job? by remember { mutableStateOf(null) }
    val triggerReconnect: () -> Unit = {
        reconnectJob?.cancel()
        reconnectJob = reconnectScope.launch { graph.connectController.reconnect() }
    }

    BoxWithConstraints(modifier = Modifier.fillMaxSize().background(tokens.background)) {
        val compact: Boolean = maxWidth < CompactBreakpoint

        if (compact) {
            // Mobile: the sidebar is a modal Sheet behind a hamburger; picking a page closes it.
            var drawerOpen: Boolean by remember { mutableStateOf(false) }

            Column(modifier = Modifier.fillMaxSize()) {
                // Compact only: a slim bar carrying the hamburger (to open the nav drawer) and channel
                // context. No page title here — the screen's own PageHeader owns that.
                TopBar(
                    channelName = user?.displayName,
                    onMenu = { drawerOpen = true },
                )
                ShellContent(
                    selected = selected,
                    graph = graph,
                    role = role,
                    onChannelDeleted = onLogout,
                    modifier = Modifier.weight(1f).fillMaxWidth(),
                )
            }

            Sheet(
                open = drawerOpen,
                onDismissRequest = { drawerOpen = false },
                side = SheetSide.Left,
            ) {
                Sidebar(
                    role = role,
                    selected = selected,
                    isAdmin = user?.isAdmin == true,
                    onSelect = {
                        requestedRoute = it
                        drawerOpen = false
                    },
                    user = user,
                    channelSwitcher = graph.channelSwitcherController,
                    languageController = languageController,
                    onLogout = onLogout,
                    onReconnect = triggerReconnect,
                )
            }
        } else {
            // Desktop / wide: persistent sidebar beside the content. The content column takes the
            // REMAINING width via weight(1f) — a fillMaxWidth/fillMaxSize child in a Row claims the
            // Row's full width (ignoring the fixed-width sidebar) and overflows the window, so the page
            // never reflows to the space left of the sidebar. weight(1f) is what makes it adapt.
            Row(modifier = Modifier.fillMaxSize()) {
                Sidebar(
                    role = role,
                    selected = selected,
                    isAdmin = user?.isAdmin == true,
                    onSelect = { requestedRoute = it },
                    user = user,
                    channelSwitcher = graph.channelSwitcherController,
                    languageController = languageController,
                    onLogout = onLogout,
                    onReconnect = triggerReconnect,
                )
                Column(modifier = Modifier.weight(1f).fillMaxHeight()) {
                    // No top bar on desktop: each screen renders its own PageHeader, so a shell-level
                    // title bar would just duplicate it. The hamburger-bearing bar exists only in the
                    // compact layout below (where the sidebar is a drawer).
                    ShellContent(
                        selected = selected,
                        graph = graph,
                        role = role,
                        onChannelDeleted = onLogout,
                        modifier = Modifier.weight(1f).fillMaxWidth(),
                    )
                }
            }
        }

        // Dead-token recovery bar — overlays the top of the shell. It AUTO-SHOWS on load when the operator's Twitch
        // token is dead (the proactive "reconnect" prompt) and stays up through an in-flight reconnect; hidden
        // otherwise. Its action runs the redirect re-auth — re-vaults a fresh token in place, no logout.
        ReconnectBanner(
            controller = graph.connectController,
            onDismiss = {
                reconnectJob?.cancel()
                graph.connectController.clearReconnectStatus()
            },
            onRetry = triggerReconnect,
            modifier = Modifier.align(Alignment.TopCenter).fillMaxWidth(),
        )

        // The professional dead-token WARNING (proactive re-auth) — a modal, not a bar. It self-dismisses once a
        // reconnect restores the token (it re-polls health while up), and its Reconnect action runs the same
        // redirect re-auth as the profile menu.
        ReauthDialog(controller = graph.connectController, onReconnect = triggerReconnect)
    }
}

// The content host. The [modifier] (weight(1f) below the top bar) is what makes the page fill and
// reflow with the window — the inner screens fill this Box, so resizing flows straight through. The resolved
// [role] threads into every screen so each gates its own write controls through `ManageGate` at the right floor
// (frontend-ia.md §7) — read stays open per the page's read floor, writes disable-with-reason below the manage
// floor. The role is non-null here (a viewer was routed to the participation surface above).
@Composable
private fun ShellContent(
    selected: ShellRoute,
    graph: AppGraph,
    role: ManagementRole,
    onChannelDeleted: () -> Unit = {},
    modifier: Modifier = Modifier,
) {
    val activeChannelId: String? by graph.channelSwitcherController.activeChannelId.collectAsStateWithLifecycle()
    key(activeChannelId) {
    Box(modifier = modifier) {
        when (selected) {
            ShellRoute.Dashboard -> HomeScreen(
                controller = graph.homeController,
                liveOpsController = graph.liveOpsController,
                hubEvents = graph.dashboardHubClient.events,
            )
            ShellRoute.Chat -> ChatScreen(
                controller = graph.chatController,
                role = role,
                hubEvents = graph.dashboardHubClient.events,
            )
            ShellRoute.Community -> CommunityScreen(controller = graph.communityController, role = role)
            ShellRoute.Commands ->
                CommandsScreen(
                    controller = graph.commandsController,
                    role = role,
                    hubEvents = graph.dashboardHubClient.events,
                )
            ShellRoute.EventResponses -> EventResponsesScreen(controller = graph.eventResponsesController, role = role)
            ShellRoute.Quotes -> QuotesScreen(controller = graph.quotesController, role = role)
            ShellRoute.Timers -> TimersScreen(controller = graph.timersController, role = role)
            ShellRoute.Moderation ->
                ModerationScreen(
                    controller = graph.moderationController,
                    role = role,
                    hubEvents = graph.dashboardHubClient.events,
                )
            ShellRoute.Analytics -> AnalyticsScreen(controller = graph.analyticsController)
            ShellRoute.Rewards ->
                RewardsScreen(
                    controller = graph.rewardsController,
                    role = role,
                    hubEvents = graph.dashboardHubClient.events,
                )
            ShellRoute.SongRequests ->
                SongRequestsScreen(
                    controller = graph.songRequestsController,
                    role = role,
                    hubEvents = graph.dashboardHubClient.events,
                )
            ShellRoute.Music ->
                MusicScreen(
                    controller = graph.musicController,
                    role = role,
                    hubEvents = graph.dashboardHubClient.events,
                )
            ShellRoute.SoundClips ->
                SoundScreen(controller = graph.soundController, role = role)
            ShellRoute.Tts -> TtsScreen(controller = graph.ttsController, role = role)
            ShellRoute.Games -> GamesScreen(controller = graph.gamesController, role = role)
            ShellRoute.Discord -> DiscordScreen(controller = graph.discordController, role = role)
            ShellRoute.Pipelines -> PipelinesScreen(controller = graph.pipelinesController, role = role)
            ShellRoute.Roles -> RolesScreen(controller = graph.rolesController, role = role)
            ShellRoute.Integrations ->
                IntegrationsScreen(
                    controller = graph.integrationsController,
                    twitchAppController = graph.twitchAppCredentialsController,
                    role = role,
                )
            ShellRoute.Settings ->
                SettingsScreen(
                    controller = graph.settingsController,
                    journalController = graph.journalPortabilityController,
                    channelBotController = graph.channelBotController,
                    billingController = graph.billingController,
                    role = role,
                    onChannelDeleted = onChannelDeleted,
                )
            ShellRoute.Economy -> EconomyScreen(controller = graph.economyController, role = role)
            ShellRoute.Alerts -> AlertsScreen(controller = graph.alertsController, role = role)
            ShellRoute.Widgets -> WidgetsScreen(controller = graph.widgetsController, role = role)
            ShellRoute.Features -> FeaturesScreen(controller = graph.featuresController, role = role)
            ShellRoute.Webhooks -> WebhooksScreen(controller = graph.webhooksController, role = role)
            ShellRoute.Federation -> FederationScreen(controller = graph.federationController, role = role)
            ShellRoute.CodeScripts -> CodeScriptsScreen(controller = graph.codeScriptsController, role = role)
            ShellRoute.CustomEvents -> CustomEventsScreen(controller = graph.customEventsController, role = role)
            ShellRoute.Admin -> AdminScreen(controller = graph.adminController)
        }
    }
    } // key(activeChannelId)
}

@Composable
private fun Sidebar(
    role: ManagementRole,
    selected: ShellRoute,
    isAdmin: Boolean,
    onSelect: (ShellRoute) -> Unit,
    user: SessionUser?,
    channelSwitcher: ChannelSwitcherController,
    languageController: LanguageController,
    onLogout: () -> Unit,
    onReconnect: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    val visible: List<NavPage> = ShellNav.visiblePagesFor(role)
    val groups: Map<NavGroup, List<NavPage>> =
        visible.filter { it.group != NavGroup.Setup }.groupBy { it.group }
    val pinned: List<NavPage> = visible.filter { it.group == NavGroup.Setup }

    Column(
        modifier = Modifier
            .fillMaxHeight()
            .width(spacing.s24 * 2.5f)
            .background(tokens.sidebar)
            .padding(horizontal = spacing.s3, vertical = spacing.s3),
    ) {
        // Channel avatar header — click to switch channels when multiple are available.
        SidebarHeader(switcher = channelSwitcher)

        Spacer(modifier = Modifier.height(spacing.s2))

        // Grouped nav. A group with more than two pages is collapsible (accordion header with a
        // rotating chevron); one- or two-page groups render as a plain, always-visible label. The
        // nav region takes the remaining height and scrolls, so every item stays reachable at any
        // window height (and inside the compact Sheet drawer, which shares this composable).
        val sectionExpanded: SnapshotStateMap<NavGroup, Boolean> = remember { mutableStateMapOf() }
        Column(
            modifier = Modifier.weight(1f).verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(spacing.s0_5),
        ) {
            groups.forEach { (group, pages) ->
                SidebarSection(
                    label = group.label(),
                    pages = pages,
                    selected = selected,
                    expanded = sectionExpanded[group] ?: true,
                    onToggleExpanded =
                        if (pages.size > 2) {
                            { sectionExpanded[group] = !(sectionExpanded[group] ?: true) }
                        } else null,
                    onSelect = onSelect,
                )
                Spacer(modifier = Modifier.height(spacing.s1))
            }
        }

        if (pinned.isNotEmpty()) {
            Separator(modifier = Modifier.padding(vertical = spacing.s2))
            SidebarSection(
                label = NavGroup.Setup.label(),
                pages = pinned,
                selected = selected,
                // Setup is a rarely-touched config group — start it collapsed.
                expanded = sectionExpanded[NavGroup.Setup] ?: false,
                onToggleExpanded =
                    if (pinned.size > 2) {
                        { sectionExpanded[NavGroup.Setup] = !(sectionExpanded[NavGroup.Setup] ?: true) }
                    } else null,
                onSelect = onSelect,
            )
        }

        if (isAdmin) {
            Separator(modifier = Modifier.padding(vertical = spacing.s2))
            NavItem(route = ShellRoute.Admin, selected = ShellRoute.Admin == selected) { onSelect(ShellRoute.Admin) }
        }

        Separator(modifier = Modifier.padding(top = spacing.s2, bottom = spacing.s2))
        ProfileBlock(
            user = user,
            role = role,
            languageController = languageController,
            onLogout = onLogout,
            onReconnect = onReconnect,
        )
    }
}

// Sidebar header — shows the active channel's avatar, display name, and an online dot.
// Tapping opens a channel-switcher dropdown when the operator has more than one channel.
// CMP 1.9.0 fixed the Wasm Popup deadlock, so DropdownMenu works correctly in the browser build.
@Composable
internal fun SidebarHeader(switcher: ChannelSwitcherController) {
    val state: SwitcherState by switcher.state.collectAsStateWithLifecycle()
    val activeId: String? by switcher.activeChannelId.collectAsStateWithLifecycle()
    val ready: SwitcherState.Ready? = state as? SwitcherState.Ready
    val channels: List<ChannelSummary> = ready?.channels ?: emptyList()
    if (channels.isEmpty()) return

    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val unregisteredModerated: List<ModeratedChannel> =
        ready?.moderatedChannels?.filter { !it.isOnboarded } ?: emptyList()
    val active: ChannelSummary? = channels.firstOrNull { it.id == activeId } ?: channels.firstOrNull()
    var expanded: Boolean by remember { mutableStateOf(false) }
    val label: String = stringResource(Res.string.shell_channel_pick)
    val name: String = active?.displayName?.takeIf { it.isNotBlank() } ?: active?.login ?: ""

    Box {
        // The channel chip is ALWAYS an interactive switcher — even with a single channel it stays a
        // discoverable control, and moderated / newly-connected channels surface in its menu as they load.
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(tokens.radius.md))
                .clickable { expanded = !expanded }
                .semantics { contentDescription = label }
                .padding(horizontal = spacing.s2, vertical = spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            // Avatar with green online indicator dot anchored at the bottom-end corner.
            Box(contentAlignment = Alignment.BottomEnd) {
                Avatar(name = name, size = spacing.s8, imageUrl = active?.profileImageUrl)
                Box(
                    modifier = Modifier
                        .size(spacing.s1_5)
                        .clip(CircleShape)
                        .background(tokens.success)
                        .border(width = spacing.s0_5, color = tokens.sidebar, shape = CircleShape),
                )
            }
            Text(
                text = name,
                style = typography.sm,
                fontWeight = FontWeight.SemiBold,
                color = tokens.sidebarForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            Icon(
                imageVector = if (expanded) ChevronUpGlyph else ChevronDownGlyph,
                contentDescription = null,
                tint = tokens.mutedForeground,
                modifier = Modifier.size(spacing.s4),
            )
        }
        DropdownMenu(
            expanded = expanded,
            onDismissRequest = { expanded = false },
            modifier = Modifier.background(tokens.popover),
        ) {
            // Every bot-registered channel is selectable; the active one is marked (bold). Re-selecting the
            // active channel is a harmless no-op — the id doesn't change, so nothing reconnects.
            channels.forEach { channel ->
                val isActive: Boolean = channel.id == active?.id
                DropdownMenuItem(
                    text = { ChannelSwitchRow(channel = channel, isActive = isActive) },
                    onClick = {
                        expanded = false
                        switcher.select(channel.id)
                    },
                )
            }
            // Twitch channels the caller moderates but where the bot is not installed — shown for context,
            // not selectable (onboarding them is a separate flow).
            if (unregisteredModerated.isNotEmpty()) {
                Separator()
                unregisteredModerated.forEach { channel ->
                    DropdownMenuItem(
                        enabled = false,
                        text = {
                            Column {
                                Text(
                                    text = channel.displayName.takeIf { it.isNotBlank() } ?: channel.login,
                                    style = typography.sm,
                                    color = tokens.mutedForeground,
                                )
                                Text(
                                    text = stringResource(Res.string.shell_channel_bot_not_installed),
                                    style = typography.xs,
                                    color = tokens.mutedForeground,
                                )
                            }
                        },
                        onClick = {},
                    )
                }
            }
        }
    }
}

// One sidebar nav group. When [onToggleExpanded] is non-null the group is collapsible: its label
// becomes a clickable accordion header with a chevron that rotates on toggle, and its pages expand/
// collapse with an animation. When null it renders as a plain, always-visible [SidebarGroupLabel]
// (used for one- and two-page groups, which aren't worth collapsing).
@Composable
private fun SidebarSection(
    label: String,
    pages: List<NavPage>,
    selected: ShellRoute,
    expanded: Boolean,
    onToggleExpanded: (() -> Unit)?,
    onSelect: (ShellRoute) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    if (onToggleExpanded != null) {
        val chevronRotation: Float by animateFloatAsState(
            targetValue = if (expanded) 0f else -90f,
            animationSpec = tween(durationMillis = CollapseDurationMillis, easing = EaseOut),
            label = "sidebarChevron",
        )
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(tokens.radius.sm))
                .clickable { onToggleExpanded() }
                .padding(start = spacing.s2, end = spacing.s1, top = spacing.s3, bottom = spacing.s1),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = label.uppercase(),
                style = typography.xs,
                color = tokens.mutedForeground,
                modifier = Modifier.weight(1f),
            )
            Icon(
                imageVector = ChevronDownGlyph,
                contentDescription = null,
                tint = tokens.mutedForeground,
                modifier = Modifier.size(spacing.s4).rotate(chevronRotation),
            )
        }
    } else if (pages.size > 1) {
        SidebarGroupLabel(label = label)
    }
    // A single-entry group (Home, Moderation, Community) drops its label and shows just the page.

    // Pages that belong to a titled group (collapsible header or plain label) are indented so they
    // read as children of that header; a single-entry group's lone page stays at the root indent.
    val hasHeader: Boolean = onToggleExpanded != null || pages.size > 1

    // shadcn Collapsible motion: a plain height expand/collapse — 200ms ease-out, no fade or spring.
    AnimatedVisibility(
        visible = onToggleExpanded == null || expanded,
        enter = expandVertically(
            animationSpec = tween(durationMillis = CollapseDurationMillis, easing = EaseOut),
            expandFrom = Alignment.Top,
        ),
        exit = shrinkVertically(
            animationSpec = tween(durationMillis = CollapseDurationMillis, easing = EaseOut),
            shrinkTowards = Alignment.Top,
        ),
    ) {
        Column(
            modifier = if (hasHeader) Modifier.padding(start = spacing.s3) else Modifier,
            verticalArrangement = Arrangement.spacedBy(spacing.s0_5),
        ) {
            pages.forEach { page ->
                NavItem(route = page.route, selected = page.route == selected) { onSelect(page.route) }
            }
        }
    }
}

// Non-collapsible group label — used for one- and two-page groups (shadcn sidebar pattern).
// The label is uppercased at render time to match the design system's category label style.
@Composable
private fun SidebarGroupLabel(label: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = label.uppercase(),
        style = typography.xs,
        color = tokens.mutedForeground,
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = spacing.s2, top = spacing.s3, bottom = spacing.s1),
    )
}

@Composable
private fun NavItem(route: ShellRoute, selected: Boolean, onClick: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // shadcn SidebarMenuButton hover: an unselected item tints to `sidebar-accent` on pointer hover;
    // the selected item keeps its primary fill (hover is a no-op on top of the active state).
    val interactionSource: MutableInteractionSource = remember { MutableInteractionSource() }
    val hovered: Boolean by interactionSource.collectIsHoveredAsState()

    val container: Color =
        when {
            selected -> tokens.sidebarPrimary
            hovered -> tokens.sidebarAccent
            else -> Color.Transparent
        }
    val content: Color =
        when {
            selected -> tokens.sidebarPrimaryForeground
            hovered -> tokens.sidebarAccentForeground
            else -> tokens.sidebarForeground
        }

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(container)
            .hoverable(interactionSource)
            .selectable(selected = selected, role = Role.Tab, onClick = onClick)
            .padding(horizontal = spacing.s3, vertical = spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Icon(
            imageVector = route.icon(),
            contentDescription = null,
            tint = content,
            modifier = Modifier.size(spacing.s4),
        )
        Text(
            text = route.label(),
            style = typography.sm,
            color = content,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
    }
}

// Profile block at the bottom of the sidebar. Clicking the profile row opens a DropdownMenu
// (language + logout) anchored to the row — Compose positions it above when there is no room
// below. CMP 1.9.0 ships a fixed Wasm Popup, so DropdownMenu works without deadlocking.
@Composable
private fun ProfileBlock(
    user: SessionUser?,
    role: ManagementRole?,
    languageController: LanguageController,
    onLogout: () -> Unit,
    onReconnect: () -> Unit,
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
                .clickable(onClick = { open = !open })
                .semantics { contentDescription = menuLabel }
                .padding(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Avatar(name = name, size = spacing.s8, imageUrl = user?.profileImageUrl)
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
        // DropdownMenu anchors to the Box that wraps the profile row. Compose positions it above
        // the row automatically when there is no room below (profile is at the bottom of the sidebar).
        DropdownMenu(
            expanded = open,
            onDismissRequest = { open = false },
            modifier = Modifier.background(tokens.popover),
        ) {
            Column(modifier = Modifier.padding(horizontal = spacing.s3, vertical = spacing.s2)) {
                Text(text = name, style = typography.sm, color = tokens.popoverForeground)
                Text(text = roleLabel, style = typography.xs, color = tokens.mutedForeground)
            }
            Separator()
            AppLanguage.entries.forEach { language ->
                DropdownMenuItem(
                    text = {
                        Text(
                            text = language.menuLabel(),
                            style = typography.sm,
                            color = tokens.popoverForeground,
                        )
                    },
                    onClick = {
                        languageController.select(language)
                        open = false
                    },
                )
            }
            Separator()
            // Reconnect Twitch — the no-logout dead-token recovery. Runs the redirect re-auth for the broadcaster
            // (device-code only on the secret-less fallback); re-vaults a fresh token in place, session kept.
            DropdownMenuItem(
                text = {
                    Text(
                        text = stringResource(Res.string.shell_reconnect_menu),
                        style = typography.sm,
                        color = tokens.popoverForeground,
                    )
                },
                onClick = {
                    open = false
                    onReconnect()
                },
            )
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

// Shows the channel or user avatar. When imageUrl is provided, renders it with a colored
// background (acts as both placeholder and error fallback — the image covers it when loaded).
@Composable
private fun Avatar(name: String, size: Dp, imageUrl: String? = null) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    val initial: String = name.trim().firstOrNull()?.uppercase() ?: "?"

    Box(
        modifier = Modifier.size(size).clip(CircleShape).background(tokens.sidebarPrimary),
        contentAlignment = Alignment.Center,
    ) {
        if (imageUrl != null) {
            AsyncImage(
                model = imageUrl,
                contentDescription = name,
                modifier = Modifier.fillMaxSize(),
                contentScale = ContentScale.Crop,
            )
        } else {
            Text(text = initial, style = typography.sm, color = tokens.sidebarPrimaryForeground)
        }
    }
}

// One row in the channel-switcher menu: the channel's avatar (with a live dot when it is streaming), its display
// name, and — the point of the switcher — the caller's OWN role on that channel as a small badge, so the operator
// sees at a glance where they broadcast vs. only moderate. The active channel is bolded and carries an accent dot.
@Composable
private fun ChannelSwitchRow(channel: ChannelSummary, isActive: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val name: String = channel.displayName.takeIf { it.isNotBlank() } ?: channel.login

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Box(contentAlignment = Alignment.BottomEnd) {
            Avatar(name = name, size = spacing.s8, imageUrl = channel.profileImageUrl)
            if (channel.isLive) {
                Box(
                    modifier = Modifier
                        .size(spacing.s1_5)
                        .clip(CircleShape)
                        .background(tokens.success)
                        .border(width = spacing.s0_5, color = tokens.popover, shape = CircleShape),
                )
            }
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = name,
                style = typography.sm,
                fontWeight = if (isActive) FontWeight.SemiBold else FontWeight.Normal,
                color = tokens.popoverForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            ChannelRoleBadge(role = channel.role)
        }
        if (isActive) {
            Box(
                modifier = Modifier.size(spacing.s2).clip(CircleShape).background(tokens.primary),
            )
        }
    }
}

// A small pill naming the caller's role on a channel (their Plane-B power there, from the channel list). Renders
// nothing when the role is unknown/blank rather than an empty chip. Role NAMES only — the operator never sees an
// internal numeric level (users-never-see-numbered-permission-levels).
@Composable
private fun ChannelRoleBadge(role: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val label: String = channelRoleLabel(role) ?: return

    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.sm))
            .background(tokens.muted)
            .padding(horizontal = spacing.s1_5, vertical = spacing.s0_5),
    ) {
        Text(text = label, style = typography.xs, color = tokens.mutedForeground, maxLines = 1)
    }
}

// Map the channel-list role string (backend sends "broadcaster" / "moderator") to its localized display name;
// null when blank so the badge is omitted. Unknown values are title-cased as a graceful fallback.
@Composable
private fun channelRoleLabel(role: String): String? =
    when (role.lowercase()) {
        "broadcaster" -> stringResource(Res.string.shell_role_broadcaster)
        "moderator" -> stringResource(Res.string.shell_role_moderator)
        "editor" -> stringResource(Res.string.shell_role_editor)
        "supermod" -> stringResource(Res.string.shell_role_supermod)
        else -> role.takeIf { it.isNotBlank() }?.replaceFirstChar { it.uppercase() }
    }

@Composable
private fun TopBar(channelName: String?, onMenu: (() -> Unit)?) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

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
            if (onMenu != null) HamburgerButton(onClick = onMenu) else Spacer(modifier = Modifier)
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                if (!channelName.isNullOrBlank()) ChannelChip(name = channelName)
                HubDot()
            }
        }
        Separator()
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

// ── Label mappings (the single place each route/group/role/language maps to its localized string) ──────────

private fun ShellRoute.icon(): ImageVector =
    when (this) {
        ShellRoute.Dashboard -> DashboardGlyph
        ShellRoute.Chat -> ChatGlyph
        ShellRoute.Commands -> CommandsGlyph
        ShellRoute.EventResponses -> EventResponsesGlyph
        ShellRoute.Pipelines -> PipelinesGlyph
        ShellRoute.Timers -> TimersGlyph
        ShellRoute.Quotes -> QuotesGlyph
        ShellRoute.CodeScripts -> CodeScriptsGlyph
        ShellRoute.Moderation -> ModerationGlyph
        ShellRoute.Rewards -> RewardsGlyph
        ShellRoute.Economy -> EconomyGlyph
        ShellRoute.Games -> GamesGlyph
        ShellRoute.Music -> MusicGlyph
        ShellRoute.SongRequests -> SongRequestsGlyph
        ShellRoute.SoundClips -> SoundClipsGlyph
        ShellRoute.Tts -> TtsGlyph
        ShellRoute.Widgets -> WidgetsGlyph
        ShellRoute.Alerts -> AlertsGlyph
        ShellRoute.Analytics -> AnalyticsGlyph
        ShellRoute.Community -> CommunityGlyph
        ShellRoute.Discord -> DiscordGlyph
        ShellRoute.Integrations -> IntegrationsGlyph
        ShellRoute.Roles -> RolesGlyph
        ShellRoute.Features -> FeaturesGlyph
        ShellRoute.Webhooks -> WebhooksGlyph
        ShellRoute.Federation -> FederationGlyph
        ShellRoute.CustomEvents -> CustomEventsGlyph
        ShellRoute.Settings -> SettingsGlyph
        ShellRoute.Admin -> AdminGlyph
    }

@Composable
private fun ShellRoute.label(): String =
    stringResource(
        when (this) {
            ShellRoute.Dashboard -> Res.string.shell_nav_dashboard
            ShellRoute.Chat -> Res.string.shell_nav_chat
            ShellRoute.Commands -> Res.string.shell_nav_commands
            ShellRoute.EventResponses -> Res.string.shell_nav_event_responses
            ShellRoute.Quotes -> Res.string.shell_nav_quotes
            ShellRoute.Timers -> Res.string.shell_nav_timers
            ShellRoute.Moderation -> Res.string.shell_nav_moderation
            ShellRoute.Rewards -> Res.string.shell_nav_rewards
            ShellRoute.Economy -> Res.string.shell_nav_economy
            ShellRoute.Games -> Res.string.shell_nav_games
            ShellRoute.SongRequests -> Res.string.shell_nav_song_requests
            ShellRoute.Music -> Res.string.shell_nav_music
            ShellRoute.SoundClips -> Res.string.shell_nav_sound
            ShellRoute.Tts -> Res.string.shell_nav_tts
            ShellRoute.Widgets -> Res.string.shell_nav_overlays
            ShellRoute.Alerts -> Res.string.shell_nav_alerts
            ShellRoute.Discord -> Res.string.shell_nav_discord
            ShellRoute.Analytics -> Res.string.shell_nav_analytics
            ShellRoute.Pipelines -> Res.string.shell_nav_pipelines
            ShellRoute.Community -> Res.string.shell_nav_community
            ShellRoute.Roles -> Res.string.shell_nav_roles
            ShellRoute.Integrations -> Res.string.shell_nav_integrations
            ShellRoute.Settings -> Res.string.shell_nav_settings
            ShellRoute.Features -> Res.string.shell_nav_features
            ShellRoute.Webhooks -> Res.string.shell_nav_webhooks
            ShellRoute.Federation -> Res.string.shell_nav_federation
            ShellRoute.CodeScripts -> Res.string.shell_nav_code_scripts
            ShellRoute.CustomEvents -> Res.string.shell_nav_custom_events
            ShellRoute.Admin -> Res.string.shell_nav_admin
        }
    )

@Composable
private fun NavGroup.label(): String =
    when (this) {
        NavGroup.Home -> stringResource(Res.string.shell_group_home)
        NavGroup.Chat -> stringResource(Res.string.shell_group_chat)
        NavGroup.Moderation -> stringResource(Res.string.shell_group_moderation)
        NavGroup.Loyalty -> stringResource(Res.string.shell_group_loyalty)
        NavGroup.Music -> stringResource(Res.string.shell_group_music)
        NavGroup.Stream -> stringResource(Res.string.shell_group_stream)
        NavGroup.Community -> stringResource(Res.string.shell_group_community)
        NavGroup.Connect -> stringResource(Res.string.shell_group_connect)
        NavGroup.Setup -> stringResource(Res.string.shell_group_setup)
    }

@Composable
private fun ManagementRole?.label(): String =
    stringResource(
        when (this) {
            ManagementRole.Moderator -> Res.string.shell_role_moderator
            ManagementRole.SuperMod -> Res.string.shell_role_supermod
            ManagementRole.Editor -> Res.string.shell_role_editor
            ManagementRole.Broadcaster -> Res.string.shell_role_broadcaster
            null -> Res.string.shell_role_viewer
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
