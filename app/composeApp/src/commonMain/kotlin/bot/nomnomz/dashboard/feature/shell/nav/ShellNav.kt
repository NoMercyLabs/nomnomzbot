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
    MultiChat,
    Commands,
    ChatTriggers,
    EventResponses,
    Quotes,
    PickLists,
    Timers,
    Moderation,
    Rewards,
    Economy,
    Games,
    Giveaways,
    SongRequests,
    Music,
    SoundClips,
    MediaShare,
    Tts,
    Widgets,
    Alerts,
    Discord,
    Analytics,
    Schedule,
    Pipelines,
    Community,
    Roles,
    Integrations,
    Features,
    Webhooks,
    Federation,
    CodeScripts,
    CustomEvents,
    Supporters,
    Obs,
    Vts,
    Automation,
    Bundles,
    Settings,
    MyData,
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
 *
 * [readActionKey] is the single backend action key that GOVERNS reading this page (e.g. `commands:read`). It is
 * the seam that lets a broadcaster-LOWERED page reach a role-less caller: visibility is `role clears readFloor`
 * OR `caller holds readActionKey` (from `ResolvedAccess.heldActionKeys`, which folds in per-action overrides).
 * `null` marks a page with no single VIP-lowerable read key — a Broadcaster-admin page (Roles, Integrations,
 * Webhooks…) or a multi-tab/ambiguous one (Settings) — which then stays gated on [readFloor] alone.
 */
data class NavPage(
    val route: ShellRoute,
    val group: NavGroup,
    val readFloor: ManagementRole,
    val manageFloor: ManagementRole?,
    val readActionKey: String?,
)

object ShellNav {

    /**
     * The binding page inventory (frontend-ia.md §3), in sidebar order. Each page's `readActionKey` is the real
     * backend `[RequireAction("…")]` key that governs reading it (verified against the V1 controllers) — the seam
     * a broadcaster lowers to delegate a page to a VIP/Sub. Broadcaster-admin pages (Roles, Integrations, Features,
     * Webhooks, Federation, CodeScripts), the multi-tab Settings, and the pages without a single governing read key
     * (SongRequests, Alerts, CustomEvents) carry `null` — they stay gated on their [NavPage.readFloor] alone.
     */
    val pages: List<NavPage> =
        listOf(
            // Top-level single-entry groups render as plain items (no header) — see SidebarSection.
            NavPage(ShellRoute.Dashboard, NavGroup.Home, ManagementRole.Moderator, null, readActionKey = "dashboard:read"),
            NavPage(ShellRoute.Chat, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "chat:read"),
            // Multi-channel watch: a mod monitors several owned/moderated channels' chats at once. Read-only
            // (null manage floor — nothing to mutate here; a mod acts from a channel's own Chat page); it reuses
            // the Chat read floor (chat:read) since it reads chat history.
            NavPage(ShellRoute.MultiChat, NavGroup.Chat, ManagementRole.Moderator, null, readActionKey = "chat:read"),
            NavPage(ShellRoute.Commands, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "commands:read"),
            // Chat triggers (keyword auto-replies) sit beside Commands in the Chat group; read at Moderator, write
            // at Editor, governed by the `chattriggers:read` action key so a broadcaster can delegate reading.
            NavPage(ShellRoute.ChatTriggers, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "chattriggers:read"),
            NavPage(ShellRoute.EventResponses, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "eventresponses:read"),
            NavPage(ShellRoute.Timers, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "timers:read"),
            NavPage(ShellRoute.Quotes, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "quotes:read"),
            NavPage(ShellRoute.PickLists, NavGroup.Chat, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "picklists:read"),
            NavPage(ShellRoute.Moderation, NavGroup.Moderation, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "moderation:read"),
            NavPage(ShellRoute.Rewards, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "reward:read"),
            NavPage(ShellRoute.Economy, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "economy:config:read"),
            NavPage(ShellRoute.Games, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "economy:games:read"),
            // Giveaways read+write floor at Moderator (giveaways:read / giveaways:write); the code-pool tools and
            // code reveal are Broadcaster-only, gated per-control inside the page on `giveaways:codes:write`.
            NavPage(ShellRoute.Giveaways, NavGroup.Loyalty, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "giveaways:read"),
            NavPage(ShellRoute.Music, NavGroup.Music, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "music:config:read"),
            NavPage(ShellRoute.SongRequests, NavGroup.Music, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = null),
            NavPage(ShellRoute.SoundClips, NavGroup.Music, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "sounds:read"),
            // Media Share: mods read + moderate the viewer clip queue (media:read / media:moderate, Moderator); the
            // config write floors at Editor (media:write), gated per-control inside the page.
            NavPage(ShellRoute.MediaShare, NavGroup.Music, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "media:read"),
            NavPage(ShellRoute.Tts, NavGroup.Music, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "tts:config:read"),
            NavPage(ShellRoute.Widgets, NavGroup.Stream, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "widget:read"),
            NavPage(ShellRoute.Alerts, NavGroup.Stream, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = null),
            NavPage(ShellRoute.Schedule, NavGroup.Stream, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "live-ops:schedule:read"),
            NavPage(ShellRoute.Pipelines, NavGroup.Stream, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "pipelines:read"),
            NavPage(ShellRoute.CodeScripts, NavGroup.Stream, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            NavPage(ShellRoute.Analytics, NavGroup.Stream, ManagementRole.Moderator, null, readActionKey = "analytics:read"),
            NavPage(ShellRoute.Community, NavGroup.Community, ManagementRole.Moderator, ManagementRole.Moderator, readActionKey = "community:read"),
            NavPage(ShellRoute.Discord, NavGroup.Connect, ManagementRole.Moderator, ManagementRole.SuperMod, readActionKey = "discord:connection:read"),
            NavPage(ShellRoute.Webhooks, NavGroup.Connect, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            NavPage(ShellRoute.Federation, NavGroup.Connect, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            NavPage(ShellRoute.CustomEvents, NavGroup.Connect, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = null),
            // Supporters: read (connections + events) floors at Moderator (supporters:read); connect / disconnect
            // are Broadcaster-only + Critical (supporters:config:write), gated per-control inside the page. It pairs
            // with Webhooks (the Ko-fi inbound endpoint) so it sits in the Connect group.
            NavPage(ShellRoute.Supporters, NavGroup.Connect, ManagementRole.Moderator, ManagementRole.Broadcaster, readActionKey = "supporters:read"),
            // OBS control: reading the connection config needs obs:config:read (Broadcaster); the page's write floor
            // is Broadcaster (obs:config:write, Critical). Scene switching (obs:control, Moderator) and streaming/
            // recording (obs:control:broadcast, Broadcaster) gate per-control inside the page at explicit floors.
            NavPage(ShellRoute.Obs, NavGroup.Connect, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = "obs:config:read"),
            // VTube Studio: read floors at Moderator (vts:config:read); connection config / authorize / rotate are
            // Broadcaster (vts:config:write); model/hotkey/expression control is Moderator (vts:control), gated inside.
            NavPage(ShellRoute.Vts, NavGroup.Connect, ManagementRole.Moderator, ManagementRole.Broadcaster, readActionKey = "vts:config:read"),
            // Automation API tokens: read floors at Editor (automation:tokens:read); every write is Broadcaster-only
            // and Critical (automation:tokens:write).
            NavPage(ShellRoute.Automation, NavGroup.Connect, ManagementRole.Editor, ManagementRole.Broadcaster, readActionKey = "automation:tokens:read"),
            // Bundles: the installed-list read floors at Moderator (bundles:read); export/import write at Editor
            // (bundles:export / bundles:import); marketplace publish + the publisher token are Broadcaster, gated
            // per-control inside the page.
            NavPage(ShellRoute.Bundles, NavGroup.Setup, ManagementRole.Moderator, ManagementRole.Editor, readActionKey = "bundles:read"),
            NavPage(ShellRoute.Integrations, NavGroup.Setup, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            NavPage(ShellRoute.Roles, NavGroup.Setup, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            NavPage(ShellRoute.Features, NavGroup.Setup, ManagementRole.Broadcaster, ManagementRole.Broadcaster, readActionKey = null),
            NavPage(ShellRoute.Settings, NavGroup.Setup, ManagementRole.Moderator, null, readActionKey = null),
            // My data: the caller's own GDPR self-service (Gate-1). No management action key — it is always the
            // signed-in user's own data — so it stays on the read floor and gates nothing per-action.
            NavPage(ShellRoute.MyData, NavGroup.Setup, ManagementRole.Moderator, null, readActionKey = null),
        )

    /**
     * The MANAGEMENT pages a caller may see (frontend-ia.md §3a/§7). A page is visible when EITHER the caller's
     * [role] clears its read floor, OR the caller holds the page's `readActionKey` in [heldActionKeys] — the
     * backend-resolved set of keys they actually clear, which folds in the broadcaster's per-action overrides. So
     * a Moderator/Editor/Broadcaster sees exactly the pages their rung clears (their shell is unchanged, since the
     * default [heldActionKeys] is empty and the role branch alone decides), while a role-LESS VIP whose broadcaster
     * lowered, say, `commands:read` sees just that page (read-only). A pure participant (null role, no management
     * read key held) sees **no** management pages and the shell renders the PARTICIPANT rung instead (Rung 0 —
     * `ParticipantNav`, gated by Plane-A community standing); [participantPagesFor] is its entry.
     */
    fun visiblePagesFor(role: ManagementRole?, heldActionKeys: Set<String> = emptySet()): List<NavPage> =
        pages.filter { it.isVisibleTo(role, heldActionKeys) }

    /**
     * Whether the caller enters the MANAGEMENT shell at all (frontend-ia.md §3a) — true iff at least one management
     * page is visible to them. This is the shell's rung fork: `false` routes a pure participant to the participant
     * rung; `true` renders the management shell (showing only their visible pages). A Moderator+ is always `true`.
     */
    fun hasManagementAccess(role: ManagementRole?, heldActionKeys: Set<String>): Boolean =
        pages.any { it.isVisibleTo(role, heldActionKeys) }

    /**
     * A page is visible when the [role] clears its read floor OR the caller holds its `readActionKey`. A page with a
     * `null` `readActionKey` (a Broadcaster-admin / multi-tab / no-single-key page) is visible on the role branch
     * only — no held key can surface it, keeping those pages on their existing floor.
     */
    private fun NavPage.isVisibleTo(role: ManagementRole?, heldActionKeys: Set<String>): Boolean =
        (role != null && role.level >= readFloor.level) ||
            (readActionKey != null && readActionKey in heldActionKeys)

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
