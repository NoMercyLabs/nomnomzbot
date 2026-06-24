// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.nav

// The shell's sidebar information architecture (frontend-ia.md §3): the fifteen content pages, the group
// each sits in, and the role floors that gate it. This is the single source the sidebar renders and the
// role gate filters — the route names mirror the §5 Route sealed interface (this model invents none). The
// page CONTENT (the NavHost screens) lands page-by-page in later slices; this defines what the shell offers
// and to whom.

/** A sidebar content page (frontend-ia.md §3). Route names mirror frontend.md §5; no new ones are invented. */
enum class ShellRoute {
    Dashboard,
    Commands,
    Timers,
    Moderation,
    Rewards,
    Economy,
    Games,
    SongRequests,
    Tts,
    Widgets,
    Alerts,
    Analytics,
    Community,
    Integrations,
    Settings,
}

/** The labelled sidebar sections, in their binding IA order; [Pinned] is the bottom Integrations + Settings. */
enum class NavGroup {
    Home,
    Chat,
    Loyalty,
    Media,
    Stream,
    Community,
    Pinned,
}

/**
 * One sidebar entry: its [route], the [group] it renders under, the [readFloor] (minimum [ManagementRole] to
 * see/open the page) and the [manageFloor] (minimum to mutate within it; null = a read-only page or per-tab
 * gating handled inside it). Action-level nuances (e.g. "create reward needs Broadcaster") live on the page.
 */
data class NavPage(
    val route: ShellRoute,
    val group: NavGroup,
    val readFloor: ManagementRole,
    val manageFloor: ManagementRole?,
)

object ShellNav {

    /** The binding page inventory (frontend-ia.md §3), in sidebar order. */
    val pages: List<NavPage> =
        listOf(
            NavPage(ShellRoute.Dashboard, NavGroup.Home, ManagementRole.Moderator, null),
            NavPage(ShellRoute.Commands, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Timers, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Moderation, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Moderator),
            NavPage(ShellRoute.Rewards, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Economy, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Games, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.SongRequests, NavGroup.Media, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Tts, NavGroup.Media, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Widgets, NavGroup.Stream, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Alerts, NavGroup.Stream, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Analytics, NavGroup.Stream, ManagementRole.Moderator, null),
            NavPage(ShellRoute.Community, NavGroup.Community, ManagementRole.Moderator, ManagementRole.Moderator),
            NavPage(ShellRoute.Integrations, NavGroup.Pinned, ManagementRole.Broadcaster, ManagementRole.Broadcaster),
            NavPage(ShellRoute.Settings, NavGroup.Pinned, ManagementRole.Moderator, null),
        )

    /** The pages a caller of [role] may see — those whose read floor the role clears (frontend-ia.md §7). */
    fun visiblePagesFor(role: ManagementRole): List<NavPage> =
        pages.filter { role.level >= it.readFloor.level }
}
