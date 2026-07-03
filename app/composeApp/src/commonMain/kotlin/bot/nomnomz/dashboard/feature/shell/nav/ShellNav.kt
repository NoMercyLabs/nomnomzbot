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

// The shell's sidebar information architecture (frontend-ia.md §3): the twenty-one content pages, the group
// each sits in, and the role floors that gate it. This is the single source the sidebar renders and the
// role gate filters — the route names mirror the §5 Route sealed interface (this model invents none). The
// page CONTENT (the NavHost screens) lands page-by-page in later slices; this defines what the shell offers
// and to whom.

/** A sidebar content page (frontend-ia.md §3). Route names mirror frontend.md §5; no new ones are invented. */
enum class ShellRoute {
    Dashboard,
    Chat,
    Commands,
    EventResponses,
    Quotes,
    Timers,
    Moderation,
    Rewards,
    Economy,
    Games,
    SongRequests,
    Music,
    SoundClips,
    Tts,
    Widgets,
    Alerts,
    Discord,
    Analytics,
    Pipelines,
    Community,
    Roles,
    Integrations,
    Features,
    Webhooks,
    Federation,
    CodeScripts,
    CustomEvents,
    Settings,
    Admin,
}

/**
 * The labelled sidebar sections, in their binding IA order (frontend-ia.md §3): eight daily-driver FEATURE
 * groups (top, scrolling) and the pinned, labelled [Setup] owner area at the bottom (divider-separated).
 */
enum class NavGroup {
    Home,
    Chat,
    Moderation,
    Loyalty,
    Music,
    Stream,
    Community,
    Connect,
    Setup,
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
            // Top-level single-entry groups render as plain items (no header) — see SidebarSection.
            NavPage(ShellRoute.Dashboard, NavGroup.Home, ManagementRole.Moderator, null),
            NavPage(ShellRoute.Moderation, NavGroup.Moderation, ManagementRole.Moderator, ManagementRole.Moderator),
            NavPage(ShellRoute.Community, NavGroup.Community, ManagementRole.Moderator, ManagementRole.Moderator),
            NavPage(ShellRoute.Chat, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Moderator),
            NavPage(ShellRoute.Commands, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.EventResponses, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Timers, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Quotes, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Rewards, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Economy, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Games, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Music, NavGroup.Music, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.SongRequests, NavGroup.Music, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.SoundClips, NavGroup.Music, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Tts, NavGroup.Music, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Widgets, NavGroup.Stream, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Alerts, NavGroup.Stream, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Pipelines, NavGroup.Stream, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.CodeScripts, NavGroup.Stream, ManagementRole.Broadcaster, ManagementRole.Broadcaster),
            NavPage(ShellRoute.Analytics, NavGroup.Stream, ManagementRole.Moderator, null),
            NavPage(ShellRoute.Discord, NavGroup.Connect, ManagementRole.Moderator, ManagementRole.SuperMod),
            NavPage(ShellRoute.Webhooks, NavGroup.Connect, ManagementRole.Broadcaster, ManagementRole.Broadcaster),
            NavPage(ShellRoute.Federation, NavGroup.Connect, ManagementRole.Broadcaster, ManagementRole.Broadcaster),
            NavPage(ShellRoute.CustomEvents, NavGroup.Connect, ManagementRole.Moderator, ManagementRole.Editor),
            NavPage(ShellRoute.Integrations, NavGroup.Setup, ManagementRole.Broadcaster, ManagementRole.Broadcaster),
            NavPage(ShellRoute.Roles, NavGroup.Setup, ManagementRole.Broadcaster, ManagementRole.Broadcaster),
            NavPage(ShellRoute.Features, NavGroup.Setup, ManagementRole.Broadcaster, ManagementRole.Broadcaster),
            NavPage(ShellRoute.Settings, NavGroup.Setup, ManagementRole.Moderator, null),
        )

    /**
     * The MANAGEMENT pages a caller of [role] may see — those whose read floor the role clears (frontend-ia.md §7).
     * A `null` [role] is a participant (no Plane-B management role): every management page floors at Moderator+, so
     * a participant sees **no** management pages here and the shell renders the PARTICIPANT rung instead (Rung 0 —
     * `ParticipantNav`, gated by Plane-A community standing). The participant surface is a real page set, not a
     * dead-end; [participantPagesFor] is its entry. The two rungs share one shell; the role just selects which.
     */
    fun visiblePagesFor(role: ManagementRole?): List<NavPage> =
        if (role == null) emptyList()
        else pages.filter { role.level >= it.readFloor.level }

    /**
     * The PARTICIPANT pages a role-less caller of [standing] sees (Rung 0). Delegates to [ParticipantNav] — the
     * single source of the participant page set — so a viewer is never a dead-end: the base surface is always
     * non-empty, and a sub/VIP unlocks additional lanes. This is the answer for `visiblePagesFor(null)`'s caller:
     * a null management role routes here, not to an empty list.
     */
    fun participantPagesFor(standing: ParticipantStanding): List<ParticipantPage> =
        ParticipantNav.pagesFor(standing)

    /**
     * The minimum [ManagementRole] to run a write [action] on the [route]'s page (frontend-ia.md §3). Most
     * controls pass [ManageAction.Default] and gate at the page's own [NavPage.manageFloor]; the called-out
     * exceptions (Rewards create/delete, Economy payout rules, Song-Request queue moderation) carry their own
     * floor here, so the rule lives in the nav model rather than scattered across screens. Returns `null` for a
     * read-only page (`manageFloor == null` and no override), where there is nothing to gate.
     */
    fun manageFloorFor(route: ShellRoute, action: ManageAction = ManageAction.Default): ManagementRole? =
        when (action) {
            // The sub-page floors the spec calls out by name (frontend-ia.md §3 Loyalty / Media rows). These do
            // not depend on the page's own floor — they are the action's own binding floor.
            ManageAction.RewardLifecycle -> ManagementRole.Broadcaster
            ManageAction.EconomyPayoutRules -> ManagementRole.Broadcaster
            ManageAction.SongQueueModeration -> ManagementRole.Moderator
            ManageAction.MusicConfig -> ManagementRole.Editor
            // The common path: gate at the page's own manage floor (null for a read-only page).
            ManageAction.Default -> pages.first { it.route == route }.manageFloor
        }

    /**
     * Whether a caller of [role] may MUTATE the [route]'s page with the given write [action] (frontend-ia.md §7,
     * "disable-with-reason for actions below the manage floor"). True iff the action has a floor and the role
     * clears it; a read-only control (no floor) and a viewer (`role == null`) can manage nothing. The screen
     * disables its write controls when this is false; the backend re-checks every write regardless.
     */
    fun canManage(
        role: ManagementRole?,
        route: ShellRoute,
        action: ManageAction = ManageAction.Default,
    ): Boolean {
        if (role == null) return false
        val floor: ManagementRole = manageFloorFor(route, action) ?: return false
        return role.level >= floor.level
    }
}
