// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.participant.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
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
import androidx.compose.material3.Text
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Sheet
import bot.nomnomz.dashboard.core.designsystem.component.SheetSide
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.di.AppGraph
import bot.nomnomz.dashboard.feature.language.state.AppLanguage
import bot.nomnomz.dashboard.feature.language.state.LanguageController
import bot.nomnomz.dashboard.feature.participant.state.ParticipantController
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantPage
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
import bot.nomnomz.dashboard.feature.shell.nav.ShellNav
import bot.nomnomz.dashboard.feature.shell.state.ChannelSwitcherController
import bot.nomnomz.dashboard.feature.shell.state.ShellAccess
import bot.nomnomz.dashboard.feature.shell.ui.ImpersonationBanner
import bot.nomnomz.dashboard.feature.shell.ui.SidebarHeader
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.app_name
import nomnomzbot.composeapp.generated.resources.language_label
import nomnomzbot.composeapp.generated.resources.language_system_default
import nomnomzbot.composeapp.generated.resources.participant_nav_games
import nomnomzbot.composeapp.generated.resources.participant_nav_leaderboards
import nomnomzbot.composeapp.generated.resources.participant_nav_me
import nomnomzbot.composeapp.generated.resources.participant_nav_my_channel
import nomnomzbot.composeapp.generated.resources.participant_nav_now_playing
import nomnomzbot.composeapp.generated.resources.participant_nav_store
import nomnomzbot.composeapp.generated.resources.participant_standing_artist
import nomnomzbot.composeapp.generated.resources.participant_standing_everyone
import nomnomzbot.composeapp.generated.resources.participant_standing_moderator
import nomnomzbot.composeapp.generated.resources.participant_standing_subscriber
import nomnomzbot.composeapp.generated.resources.participant_standing_vip
import nomnomzbot.composeapp.generated.resources.shell_nav_menu_open
import nomnomzbot.composeapp.generated.resources.shell_preview_banner
import nomnomzbot.composeapp.generated.resources.shell_preview_exit
import nomnomzbot.composeapp.generated.resources.shell_profile_logout
import nomnomzbot.composeapp.generated.resources.shell_profile_open
import nomnomzbot.composeapp.generated.resources.shell_topbar_channel_label
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The PARTICIPANT rung's shell (Rung 0) — the SAME grouped-sidebar + top-bar + profile-block chrome as the
// management shell, rendered for a role-less viewer/sub/VIP. It is not a fork: it is the user-first surface of the
// one shell, with a participant page set (gated by Plane-A community standing, not a management role) and
// read-mostly + self-service screens. A single [ParticipantController], built once from the resolved access
// (channel + caller's own user GUID + standing + permit capabilities), backs every page.
private val CompactBreakpoint: Dp = 720.dp

@Composable
fun ParticipantShell(
    graph: AppGraph,
    languageController: LanguageController,
    user: SessionUser?,
    access: ShellAccess.Resolved,
    onLogout: () -> Unit,
    onExitPreview: (() -> Unit)? = null,
) {
    val tokens = LocalTokens.current

    // One controller for the whole participant surface, built from the resolved access and kept for its lifetime.
    val controller: ParticipantController =
        remember(access.channelId, access.userId) {
            graph.participantController(
                channelId = access.channelId,
                userId = access.userId,
                standing = access.standing,
                capabilities = access.capabilities,
            )
        }

    var selected: ParticipantPage by remember { mutableStateOf(ParticipantPage.MyChannel) }
    val visible: List<ParticipantPage> = ShellNav.participantPagesFor(access.standing)

    // Exit affordances that ride ABOVE the participant surface: the admin act-as banner (an operator who
    // impersonates a role-less viewer lands here, so the Exit control must live on THIS surface too, not only the
    // management shell) and the manager's preview banner. Both are hidden for an ordinary viewer.
    val exitScope: CoroutineScope = rememberCoroutineScope()
    Column(modifier = Modifier.fillMaxSize()) {
        ImpersonationBanner(
            sessionStore = graph.sessionStore,
            onExit = { exitScope.launch { graph.adminController.exitImpersonation() } },
        )
        onExitPreview?.let { PreviewBanner(onExit = it) }

    BoxWithConstraints(modifier = Modifier.weight(1f).fillMaxWidth().background(tokens.background)) {
        val compact: Boolean = maxWidth < CompactBreakpoint

        if (compact) {
            var drawerOpen: Boolean by remember { mutableStateOf(false) }

            Column(modifier = Modifier.fillMaxSize()) {
                TopBar(
                    title = selected.label(),
                    channelName = user?.displayName,
                    onMenu = { drawerOpen = true },
                )
                Content(
                    selected = selected,
                    controller = controller,
                    modifier = Modifier.weight(1f).fillMaxWidth(),
                )
            }

            Sheet(
                open = drawerOpen,
                onDismissRequest = { drawerOpen = false },
                side = SheetSide.Left,
            ) {
                Sidebar(
                    pages = visible,
                    selected = selected,
                    standing = access.standing,
                    switcher = graph.channelSwitcherController,
                    onSelect = {
                        selected = it
                        drawerOpen = false
                    },
                    user = user,
                    languageController = languageController,
                    onLogout = onLogout,
                )
            }
        } else {
            Row(modifier = Modifier.fillMaxSize()) {
                Sidebar(
                    pages = visible,
                    selected = selected,
                    standing = access.standing,
                    switcher = graph.channelSwitcherController,
                    onSelect = { selected = it },
                    user = user,
                    languageController = languageController,
                    onLogout = onLogout,
                )
                Column(modifier = Modifier.weight(1f).fillMaxHeight()) {
                    TopBar(title = selected.label(), channelName = user?.displayName, onMenu = null)
                    Content(
                        selected = selected,
                        controller = controller,
                        modifier = Modifier.weight(1f).fillMaxWidth(),
                    )
                }
            }
        }
    }
    }
}

// A thin banner above the participant surface telling a manager they are PREVIEWING the viewer experience, with an
// Exit back to their dashboard. Mirrors the act-as [ImpersonationBanner] styling; only rendered in preview mode.
@Composable
private fun PreviewBanner(onExit: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(tokens.accent)
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.shell_preview_banner),
            style = typography.sm,
            fontWeight = FontWeight.Medium,
            color = tokens.accentForeground,
            modifier = Modifier.weight(1f),
        )
        Text(
            text = stringResource(Res.string.shell_preview_exit),
            style = typography.sm,
            fontWeight = FontWeight.SemiBold,
            color = tokens.accentForeground,
            modifier = Modifier
                .clip(RoundedCornerShape(tokens.radius.sm))
                .clickable(onClick = onExit)
                .padding(horizontal = spacing.s2, vertical = spacing.s1),
        )
    }
}

// Routes the selected participant page to its screen. Each screen is a read-mostly + self-service slice of an
// existing API through the shared [ParticipantController]; there are no management controls on this surface.
@Composable
private fun Content(
    selected: ParticipantPage,
    controller: ParticipantController,
    modifier: Modifier = Modifier,
) {
    Box(modifier = modifier) {
        when (selected) {
            ParticipantPage.MyChannel -> MyChannelScreen(controller = controller)
            ParticipantPage.NowPlaying -> NowPlayingScreen(controller = controller)
            ParticipantPage.Leaderboards -> LeaderboardsScreen(controller = controller)
            ParticipantPage.PointsAndStore -> PointsAndStoreScreen(controller = controller)
            ParticipantPage.Games -> ParticipantGamesScreen(controller = controller)
            ParticipantPage.Me -> MeScreen(controller = controller)
        }
    }
}

@Composable
private fun Sidebar(
    pages: List<ParticipantPage>,
    selected: ParticipantPage,
    standing: ParticipantStanding,
    switcher: ChannelSwitcherController,
    onSelect: (ParticipantPage) -> Unit,
    user: SessionUser?,
    languageController: LanguageController,
    onLogout: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

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

        // Always-present channel switcher — the SAME chip as the management shell. A participant here is a
        // viewer, or a moderator currently viewing a channel they mod; either way they must be able to switch
        // back to a channel they broadcast/moderate instead of being stuck on this surface.
        SidebarHeader(switcher = switcher)
        Spacer(modifier = Modifier.height(spacing.s2))

        Column(
            modifier = Modifier.weight(1f).verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            pages.forEach { page ->
                NavItem(page = page, selected = page == selected) { onSelect(page) }
            }
        }

        Separator(modifier = Modifier.padding(top = spacing.s2, bottom = spacing.s2))
        ProfileBlock(
            user = user,
            standing = standing,
            languageController = languageController,
            onLogout = onLogout,
        )
    }
}

@Composable
private fun NavItem(page: ParticipantPage, selected: Boolean, onClick: () -> Unit) {
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
        Text(text = page.label(), style = typography.sm, color = content)
    }
}

@Composable
private fun ProfileBlock(
    user: SessionUser?,
    standing: ParticipantStanding,
    languageController: LanguageController,
    onLogout: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var open: Boolean by remember { mutableStateOf(false) }
    val menuLabel: String = stringResource(Res.string.shell_profile_open)
    val name: String = user?.displayName ?: ""
    val standingLabel: String = stringResource(standing.labelResource())

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
                    text = standingLabel,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }

        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            Column(modifier = Modifier.padding(horizontal = spacing.s3, vertical = spacing.s2)) {
                Text(text = name, style = typography.sm, color = tokens.popoverForeground)
                Text(text = standingLabel, style = typography.xs, color = tokens.mutedForeground)
            }
            Separator()

            AppLanguage.entries.forEach { language ->
                DropdownMenuItem(
                    text = { Text(text = language.menuLabel(), style = typography.sm) },
                    onClick = {
                        languageController.select(language)
                        open = false
                    },
                )
            }
            Separator()

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
            if (!channelName.isNullOrBlank()) ChannelChip(name = channelName)
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

// ── Label mappings ────────────────────────────────────────────────────────────

@Composable
private fun ParticipantPage.label(): String =
    stringResource(
        when (this) {
            ParticipantPage.MyChannel -> Res.string.participant_nav_my_channel
            ParticipantPage.NowPlaying -> Res.string.participant_nav_now_playing
            ParticipantPage.Leaderboards -> Res.string.participant_nav_leaderboards
            ParticipantPage.PointsAndStore -> Res.string.participant_nav_store
            ParticipantPage.Games -> Res.string.participant_nav_games
            ParticipantPage.Me -> Res.string.participant_nav_me
        }
    )

/** The localized label for a community standing — shared by the profile badge and the My-Channel header. */
internal fun ParticipantStanding.labelResource(): StringResource =
    when (this) {
        ParticipantStanding.Everyone -> Res.string.participant_standing_everyone
        ParticipantStanding.Subscriber -> Res.string.participant_standing_subscriber
        ParticipantStanding.Vip -> Res.string.participant_standing_vip
        ParticipantStanding.Artist -> Res.string.participant_standing_artist
        ParticipantStanding.Moderator -> Res.string.participant_standing_moderator
    }

@Composable
private fun AppLanguage.menuLabel(): String =
    when (this) {
        AppLanguage.System ->
            "${stringResource(Res.string.language_label)}: ${stringResource(Res.string.language_system_default)}"
        AppLanguage.English -> "English"
        AppLanguage.Dutch -> "Nederlands"
    }
