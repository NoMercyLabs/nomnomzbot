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

import bot.nomnomz.dashboard.core.navigation.ShellPage

// The shell's sidebar information architecture (frontend-ia.md section 3): the group each page sits in and the role
// floors that gate it. This is the single source the sidebar renders and the role gate filters. The sidebar shows
// one row per PAGE; a page's tabs are not rows (the tab strip shows them). My data and Admin are not pages of the
// sidebar: they open from the profile menu (see [ShellNav.isProfileMenuRoute]).

/**
 * The labelled sidebar sections, in their binding IA order (frontend-ia.md section 3.3): Home (no header), six
 * feature groups, and the pinned, collapsed [Setup] owner area at the bottom.
 */
enum class NavGroup {
    Home,
    Live,
    Build,
    People,
    Loyalty,
    MusicVoice,
    Connect,
    Setup,
}

/**
 * One gated route of the sidebar: its [route], the [group] it renders under, the [readFloor] (minimum
 * [ManagementRole] to see/open the page) and the [manageFloor] (minimum to mutate within it; null = a read-only page
 * or per-tab gating handled inside it). Action-level nuances (e.g. "create reward needs Broadcaster") live on the page.
 *
 * [readActionKey] is the single backend action key that GOVERNS reading this page (e.g. `commands:read`). It is
 * the seam that lets a broadcaster-LOWERED page reach a role-less caller: visibility is `role clears readFloor`
 * OR `caller holds readActionKey` (from `ResolvedAccess.heldActionKeys`, which folds in per-action overrides).
 * `null` marks a page with no single VIP-lowerable read key, which then stays gated on [readFloor] alone.
 */
data class NavPage(
    val route: ShellRoute,
    val group: NavGroup,
    val readFloor: ManagementRole,
    val manageFloor: ManagementRole?,
    val readActionKey: String?,
)

/** One sidebar row: a [page] and the [route] the row opens (the first tab the caller may read). */
data class NavRow(val page: ShellPage, val route: ShellRoute)

/** One sidebar section: a [group] and its [rows], in sidebar order. */
data class NavSection(val group: NavGroup, val rows: List<NavRow>)

object ShellNav {

    /**
     * The binding route inventory (frontend-ia.md section 3.3), in sidebar order: grouped by [NavGroup], pages in
     * the order of their group, tabs in the order of their page. Each route's `readActionKey` is the real backend
     * `[RequireAction("...")]` key that governs reading it. Broadcaster-admin routes and routes without a single
     * governing read key carry `null` and stay gated on their [NavPage.readFloor] alone.
     */
    val pages: List<NavPage> =
        listOf(
            NavPage(ShellRoute.Home, NavGroup.Home, ManagementRole.Moderator, null, readActionKey = "dashboard:read"),
            // Live: what you use while the stream runs.
            NavPage(ShellRoute.ChatChannel, NavGroup.Live, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "chat:read"),
            // All my channels (was Multi-Chat): read-only, reuses the Chat read floor.
            NavPage(ShellRoute.ChatAll, NavGroup.Live, ManagementRole.Moderator, null, readActionKey = "chat:read"),
            NavPage(ShellRoute.StreamInfo, NavGroup.Live, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "stream:read"),
            NavPage(ShellRoute.StreamInteractions, NavGroup.Live, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "chatpolls:read"),
            NavPage(ShellRoute.StreamSchedule, NavGroup.Live, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "live-ops:schedule:read"),
            // OBS control: reading the connection config needs obs:config:read (Broadcaster); the page's write floor
            // is Broadcaster (obs:config:write, Critical). Scene switching (obs:control, Moderator) and streaming/
            // recording (obs:control:broadcast, Broadcaster) gate per-control inside the page at explicit floors.
            NavPage(ShellRoute.Obs, NavGroup.Live, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = "obs:config:read"),
            // VTube Studio: read floors at Moderator (vts:config:read); connection config / authorize / rotate are
            // Broadcaster (vts:config:write); model/hotkey/expression control is Moderator (vts:control), gated inside.
            NavPage(ShellRoute.VtubeStudio, NavGroup.Live, ManagementRole.Moderator, ManagementRole.Broadcaster, readActionKey = "vts:config:read"),
            NavPage(ShellRoute.Analytics, NavGroup.Live, ManagementRole.Moderator, null, readActionKey = "analytics:read"),
            // Build: things you make once, and the bot then runs by itself.
            // Command management is Moderator, mirroring the backend commands:write floor.
            NavPage(ShellRoute.Commands, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "commands:read"),
            NavPage(ShellRoute.EventResponsesResponses, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "eventresponses:read"),
            NavPage(ShellRoute.EventResponsesAlertQueue, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = null),
            // Triggers & Timers: read at Moderator, write at Editor, governed by the `<kind>:read` action keys so a
            // broadcaster can delegate reading.
            NavPage(ShellRoute.TriggersChat, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "chattriggers:read"),
            NavPage(ShellRoute.TriggersVoice, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "voicetriggers:read"),
            NavPage(ShellRoute.TriggersTimers, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "timers:read"),
            NavPage(ShellRoute.Pipelines, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "pipelines:read"),
            NavPage(ShellRoute.Scripts, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "code:script:author"),
            NavPage(ShellRoute.Overlays, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "widget:read"),
            // Library: Sounds and Files share the sound-clip action pair (sounds:read / sounds:write).
            NavPage(ShellRoute.LibrarySounds, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "sounds:read"),
            NavPage(ShellRoute.LibraryFiles, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "sounds:read"),
            NavPage(ShellRoute.LibraryRandomResponses, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "picklists:read"),
            NavPage(ShellRoute.LibraryQuotes, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "quotes:read"),
            // Gallery, Bundles tab: the installed-list read floors at Moderator (bundles:read); export/import write at
            // Moderator; marketplace publish + the publisher token are Broadcaster, gated per-control inside the page.
            // Gallery, Templates tab: browsing the catalogue reads at the same bundles:read floor as the installed list.
            NavPage(ShellRoute.GalleryTemplates, NavGroup.Build, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "bundles:read"),
            NavPage(ShellRoute.GalleryBundles,NavGroup.Build, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "bundles:read"),
            // People: one page, six tabs. The moderation tabs share `moderation:read`; only the MANAGE floor differs.
            NavPage(ShellRoute.PeopleOverview, NavGroup.People, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "moderation:read"),
            NavPage(ShellRoute.PeopleQueue, NavGroup.People, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "moderation:read"),
            NavPage(ShellRoute.PeopleViewers, NavGroup.People, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "community:read"),
            NavPage(ShellRoute.PeopleHistory, NavGroup.People, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "moderation:read"),
            // Editor to MANAGE: changing the rules that govern every future enforcement is a different act from
            // applying one. Reading stays at Moderator so a mod can see the rules they are enforcing.
            NavPage(ShellRoute.PeopleRules, NavGroup.People, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "moderation:read"),
            NavPage(ShellRoute.PeopleRoles, NavGroup.People, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            NavPage(ShellRoute.ChannelPoints, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "reward:read"),
            NavPage(ShellRoute.Economy, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "economy:config:read"),
            NavPage(ShellRoute.Games, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "economy:games:read"),
            // Giveaways read+write floor at Moderator; the code-pool tools and code reveal are Broadcaster-only,
            // gated per-control inside the page on `giveaways:codes:write`.
            NavPage(ShellRoute.Giveaways, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "giveaways:read"),
            NavPage(ShellRoute.MusicNowPlaying, NavGroup.MusicVoice, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "music:config:read"),
            NavPage(ShellRoute.MusicSongRequests, NavGroup.MusicVoice, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = null),
            NavPage(ShellRoute.TtsLive, NavGroup.MusicVoice, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "tts:config:read"),
            NavPage(ShellRoute.TtsVoices, NavGroup.MusicVoice, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "tts:config:read"),
            NavPage(ShellRoute.TtsRules, NavGroup.MusicVoice, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "tts:config:read"),
            NavPage(ShellRoute.TtsProvider, NavGroup.MusicVoice, ManagementRole.Editor, ManagementRole.Editor, readActionKey = null),
            // Media Share: mods read + moderate the viewer clip queue; the config write floors at Editor, gated inside.
            NavPage(ShellRoute.MediaShare, NavGroup.MusicVoice, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "media:read"),
            NavPage(ShellRoute.Discord, NavGroup.Connect, ManagementRole.Moderator, ManagementRole.LeadModerator, readActionKey = "discord:connection:read"),
            NavPage(ShellRoute.Webhooks, NavGroup.Connect, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            NavPage(ShellRoute.CustomEvents, NavGroup.Connect, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = null),
            // Supporters: read floors at Moderator; connect / disconnect are Broadcaster-only + Critical, gated inside.
            NavPage(ShellRoute.Supporters, NavGroup.Connect, ManagementRole.Moderator, ManagementRole.Broadcaster, readActionKey = "supporters:read"),
            NavPage(ShellRoute.Federation, NavGroup.Connect, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            // API & devices: read floors at Editor (automation:tokens:read); every write is Broadcaster-only and Critical.
            NavPage(ShellRoute.ApiDevices, NavGroup.Connect, ManagementRole.Editor, ManagementRole.Broadcaster, readActionKey = "automation:tokens:read"),
            NavPage(ShellRoute.Integrations, NavGroup.Setup, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            NavPage(ShellRoute.Features, NavGroup.Setup, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            NavPage(ShellRoute.Settings, NavGroup.Setup, ManagementRole.Moderator, null, readActionKey = null),
        )

    /**
     * The MANAGEMENT routes a caller may see (frontend-ia.md section 3.3/7). A route is visible when EITHER the
     * caller's [role] clears its read floor, OR the caller holds the route's `readActionKey` in [heldActionKeys] - the
     * backend-resolved set of keys they actually clear, which folds in the broadcaster's per-action overrides. A pure
     * participant (null role, no management read key held) sees no management routes and the shell renders the
     * PARTICIPANT rung instead ([participantPagesFor]).
     */
    fun visiblePagesFor(role: ManagementRole?, heldActionKeys: Set<String> = emptySet()): List<NavPage> =
        pages.filter { it.isVisibleTo(role, heldActionKeys) }

    /**
     * The sidebar a caller sees: one [NavSection] per non-empty group, in group order, one [NavRow] per page. A row
     * opens the first tab the caller may read, so a page whose first tab is above their floor never opens a screen
     * they cannot use.
     */
    fun sidebarFor(role: ManagementRole?, heldActionKeys: Set<String> = emptySet()): List<NavSection> {
        val visible: List<NavPage> = visiblePagesFor(role, heldActionKeys)
        return NavGroup.entries.mapNotNull { group: NavGroup ->
            val rows: List<NavRow> =
                visible
                    .filter { it.group == group }
                    .groupBy { it.route.page }
                    .map { (page: ShellPage, routes: List<NavPage>) ->
                        NavRow(page, routes.minBy { page.tabs.indexOf(it.route.tab).coerceAtLeast(0) }.route)
                    }
            if (rows.isEmpty()) null else NavSection(group, rows)
        }
    }

    /** Whether [route] opens from the profile menu (My data, Admin) rather than from the sidebar. */
    fun isProfileMenuRoute(route: ShellRoute): Boolean = route == ShellRoute.MyData || route == ShellRoute.Admin

    /**
     * Whether the caller enters the MANAGEMENT shell at all (frontend-ia.md section 3a) - true iff at least one
     * management route is visible to them. `false` routes a pure participant to the participant rung.
     */
    fun hasManagementAccess(role: ManagementRole?, heldActionKeys: Set<String>): Boolean =
        pages.any { it.isVisibleTo(role, heldActionKeys) }

    /**
     * A route is visible when the [role] clears its read floor OR the caller holds its `readActionKey`. A `null`
     * `readActionKey` is visible on the role branch only, keeping those routes on their existing floor.
     */
    private fun NavPage.isVisibleTo(role: ManagementRole?, heldActionKeys: Set<String>): Boolean =
        (role != null && role.level >= readFloor.level) ||
            (readActionKey != null && readActionKey in heldActionKeys)

    /**
     * The PARTICIPANT pages a role-less caller of [standing] sees (Rung 0). Delegates to [ParticipantNav] - the
     * single source of the participant page set - so a viewer is never a dead-end.
     */
    fun participantPagesFor(standing: ParticipantStanding): List<ParticipantPage> =
        ParticipantNav.pagesFor(standing)

    /**
     * The minimum [ManagementRole] to run a write [action] on the [route]'s page (frontend-ia.md section 3). Most
     * controls pass [ManageAction.Default] and gate at the page's own [NavPage.manageFloor]; the called-out
     * exceptions carry their own floor here, so the rule lives in the nav model rather than scattered across
     * screens. Returns `null` for a read-only page, where there is nothing to gate.
     */
    fun manageFloorFor(route: ShellRoute, action: ManageAction = ManageAction.Default): ManagementRole? =
        when (action) {
            ManageAction.RewardLifecycle -> ManagementRole.Broadcaster
            ManageAction.EconomyPayoutRules -> ManagementRole.Moderator
            ManageAction.SongQueueModeration -> ManagementRole.Moderator
            ManageAction.MusicConfig -> ManagementRole.Moderator
            // The common path: gate at the page's own manage floor (null for a read-only page).
            ManageAction.Default -> pages.first { it.route == route }.manageFloor
        }

    /**
     * Whether a caller of [role] may MUTATE the [route]'s page with the given write [action] (frontend-ia.md section
     * 7, "disable-with-reason for actions below the manage floor"). True iff the action has a floor and the role
     * clears it; a read-only control and a viewer (`role == null`) can manage nothing. The backend re-checks every
     * write regardless.
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
