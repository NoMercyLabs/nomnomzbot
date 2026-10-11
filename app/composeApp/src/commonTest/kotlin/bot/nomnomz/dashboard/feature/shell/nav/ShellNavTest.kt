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
import bot.nomnomz.dashboard.feature.shell.ui.channelRoleLabelResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_role_lead_moderator
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves the binding Plane-B role gate of the shell sidebar (frontend-ia.md §3/§7): a sidebar item renders
// only if the caller's ManagementRole >= the page's read floor. This IS the routing/visibility decision, so
// if it breaks a Mod would see Broadcaster-only pages (or a Broadcaster would lose pages). The UI merely
// renders this model, so testing the model proves the gate.
class ShellNavTest {

    @Test
    fun broadcaster_sees_every_page_including_the_broadcaster_floored_integrations() {
        val visible: List<NavPage> = ShellNav.visiblePagesFor(ManagementRole.Broadcaster)

        assertEquals(ShellNav.pages.size, visible.size) // the Broadcaster floor clears every page
        assertTrue(visible.any { it.route == ShellRoute.Integrations })
        assertTrue(visible.any { it.route == ShellRoute.Settings })
    }

    @Test
    fun moderator_sees_every_page_except_those_floored_above_moderator() {
        val visible: List<NavPage> = ShellNav.visiblePagesFor(ManagementRole.Moderator)

        // The Broadcaster-floored pages (Integrations, Roles, OBS) must be hidden from a Mod.
        assertFalse(visible.any { it.route == ShellRoute.Integrations })
        assertFalse(visible.any { it.route == ShellRoute.PeopleRoles })
        assertFalse(visible.any { it.route == ShellRoute.ObsLiveControl })
        assertFalse(visible.any { it.route == ShellRoute.ObsAdvanced })
        // The Editor-floored Automation (automation:tokens:read = Editor) is also above a Mod.
        assertFalse(visible.any { it.route == ShellRoute.ApiDevices })
        // Everything at the Moderator floor stays visible — incl. Discord (read floor lowered
        // Broadcaster→Moderator, frontend-ia.md §3 Connect), VTube Studio (vts:config:read = Moderator),
        // Moderation, and the Moderator-floored Settings.
        assertTrue(visible.any { it.route == ShellRoute.Home })
        assertTrue(visible.any { it.route == ShellRoute.Discord })
        assertTrue(visible.any { it.route == ShellRoute.VtubeStudio })
        assertTrue(visible.any { it.route == ShellRoute.PeopleOverview })
        assertTrue(visible.any { it.route == ShellRoute.Settings })
        // Exactly the pages floored ABOVE Moderator are dropped — no more, no fewer.
        val abvModerator: Int =
            ShellNav.pages.count { it.readFloor.level > ManagementRole.Moderator.level }
        assertEquals(ShellNav.pages.size - abvModerator, visible.size)
    }

    @Test
    fun editor_sees_the_moderator_pages_plus_the_editor_floored_automation() {
        val editor: Set<ShellRoute> = ShellNav.visiblePagesFor(ManagementRole.Editor).map { it.route }.toSet()
        val moderator: Set<ShellRoute> =
            ShellNav.visiblePagesFor(ManagementRole.Moderator).map { it.route }.toSet()

        // An Editor sees every page a Moderator sees, plus the single Editor-floored page (Automation API
        // tokens — automation:tokens:read = Editor). No other page sits between the Moderator and Editor floors.
        assertTrue(editor.containsAll(moderator))
        assertEquals(setOf(ShellRoute.ApiDevices, ShellRoute.TtsProvider), editor - moderator)
    }

    @Test
    fun pages_are_grouped_in_the_binding_ia_order_with_setup_pinned_last() {
        val groupsInOrder: List<NavGroup> = ShellNav.pages.map { it.group }.distinct()

        assertEquals(
            listOf(
                NavGroup.Home,
                NavGroup.Live,
                NavGroup.Build,
                NavGroup.People,
                NavGroup.Loyalty,
                NavGroup.MusicVoice,
                NavGroup.Connect,
                NavGroup.Setup,
            ),
            groupsInOrder,
        )
    }

    @Test
    fun music_and_voice_shows_one_music_row_and_no_separate_song_requests_row() {
        val section: NavSection =
            ShellNav.sidebarFor(ManagementRole.Editor).single { it.group == NavGroup.MusicVoice }

        val musicRows: List<NavRow> = section.rows.filter { it.page == ShellPage.Music }
        assertEquals(1, musicRows.size)
        assertEquals(ShellRoute.MusicNowPlaying, musicRows.single().route)
        assertTrue(section.rows.none { it.route == ShellRoute.MusicSongRequests })
        assertEquals(listOf("now-playing", "song-requests"), ShellPage.Music.tabs)
    }

    @Test
    fun live_shows_one_stream_row_for_a_moderator_and_no_row_for_the_stream_tabs() {
        val section: NavSection =
            ShellNav.sidebarFor(ManagementRole.Moderator).single { it.group == NavGroup.Live }

        val streamRows: List<NavRow> = section.rows.filter { it.page == ShellPage.Stream }
        assertEquals(1, streamRows.size)
        assertEquals(ShellRoute.StreamInfo, streamRows.single().route)
        assertTrue(section.rows.none { it.route == ShellRoute.StreamInteractions })
        assertTrue(section.rows.none { it.route == ShellRoute.StreamSchedule })
        assertEquals(listOf("info-presets", "interactions", "schedule"), ShellPage.Stream.tabs)
    }

    @Test
    fun a_moderator_manages_every_stream_tab() {
        listOf(ShellRoute.StreamInfo, ShellRoute.StreamInteractions, ShellRoute.StreamSchedule).forEach { route ->
            assertTrue(ShellNav.canManage(ManagementRole.Moderator, route), "Moderator manages $route")
        }
    }

    @Test
    fun every_page_with_a_manage_floor_keeps_it_at_or_above_its_read_floor() {
        // A page can never be manageable by a role that can't even see it.
        ShellNav.pages.forEach { page ->
            val manage: ManagementRole? = page.manageFloor
            if (manage != null) {
                assertTrue(
                    manage.level >= page.readFloor.level,
                    "manage floor below read floor for ${page.route}",
                )
            }
        }
    }

    // ── Participant (no management role) — Rung 0 is a REAL surface, not a dead-end ────────────────────────

    @Test
    fun a_viewer_with_no_management_role_sees_no_management_pages() {
        // Every shell page floors at Moderator+, so a role-less viewer sees NO management pages — but they are not
        // a dead-end: they get the participant rung instead. The management page set for a null role is empty.
        val visible: List<NavPage> = ShellNav.visiblePagesFor(null)

        assertTrue(visible.isEmpty(), "a viewer must see no management pages, got $visible")
    }

    @Test
    fun a_role_less_viewer_gets_a_non_empty_participant_page_set() {
        // The headline of the IA redesign: a role-less viewer is routed to the participant rung, whose page set is
        // a REAL surface (My Channel, Now Playing, Leaderboards, Store, Games, Me) — never an empty dead-end.
        val participant: List<ParticipantPage> = ShellNav.participantPagesFor(ParticipantStanding.Everyone)

        assertTrue(participant.isNotEmpty(), "a participant must see a real page set, got $participant")
        assertTrue(participant.contains(ParticipantPage.MyChannel))
        assertTrue(participant.contains(ParticipantPage.PointsAndStore))
        assertTrue(participant.contains(ParticipantPage.Me))
    }

    @Test
    fun a_viewer_can_manage_nothing() {
        // With no management role, every page's write affordances are gated off — there is nothing to manage.
        ShellNav.pages.forEach { page ->
            assertFalse(ShellNav.canManage(null, page.route), "viewer could manage ${page.route}")
        }
    }

    // ── Per-page manage-floor gate (the gated-action set per role, frontend-ia.md §7) ─────────────────────

    @Test
    fun moderator_can_manage_only_the_moderator_floored_pages() {
        // A Mod sees the shell (minus Broadcaster pages) but may only MUTATE pages whose manage floor is
        // Moderator — Chat, Commands, Moderation, Community, Giveaways (giveaways:write floors at Moderator; the
        // code-pool tools inside it are Broadcaster-only, gated per-control). Command management floors at
        // Moderator to match StreamElements/Nightbot/Cloudbot/Fossabot (mods manage commands out of the box).
        // Quotes/Timers/EventResponses etc. (Editor manage floor) stay read-only to a Mod (the write button
        // disables with "Requires Editor").
        val manageable: Set<ShellRoute> =
            ShellNav.pages
                .filter { ShellNav.canManage(ManagementRole.Moderator, it.route) }
                .map { it.route }
                .toSet()

        assertEquals(
            setOf(
                ShellRoute.ChatChannel,
                ShellRoute.Commands,
                ShellRoute.PeopleOverview,
                // The moderation split (S-UX-3): the desk, the queue and history all ACT at Moderator.
                // ModerationRules is deliberately absent — changing the rules that govern every future
                // enforcement floors at Editor, so a Mod reads them and cannot rewrite them.
                ShellRoute.PeopleQueue,
                ShellRoute.PeopleHistory,
                ShellRoute.PeopleViewers,
                ShellRoute.Giveaways,
                // Media Share: the mod clip queue moderate actions floor at Moderator (media:moderate); the
                // Settings tab writes the config, which floors at Editor, so it is absent here.
                ShellRoute.MediaShareQueue,
                // S-IA-MOD-FLOORS: moderators manage stream and channel behaviors (the server floors these
                // actions at Moderator): Code Scripts, Economy, Games, Music, Schedule and Bundles.
                ShellRoute.Scripts,
                ShellRoute.Economy,
                ShellRoute.Games,
                ShellRoute.MusicNowPlaying,
                ShellRoute.StreamInfo,
                ShellRoute.StreamInteractions,
                ShellRoute.StreamSchedule,
                ShellRoute.GalleryTemplates,
                ShellRoute.GalleryBundles,
            ),
            manageable,
        )
    }

    @Test
    fun editor_can_manage_every_editor_and_moderator_floored_page_but_not_the_broadcaster_ones() {
        val manageable: Set<ShellRoute> =
            ShellNav.pages
                .filter { ShellNav.canManage(ManagementRole.Editor, it.route) }
                .map { it.route }
                .toSet()

        // An Editor can manage the Editor-floored content pages...
        assertTrue(manageable.contains(ShellRoute.Commands))
        assertTrue(manageable.contains(ShellRoute.ChannelPoints))
        assertTrue(manageable.contains(ShellRoute.Pipelines))
        // ...and still the Moderator-floored ones (Editor > Moderator on the ladder)...
        assertTrue(manageable.contains(ShellRoute.PeopleOverview))
        // ...and the Lead-Moderator-floored Discord (Editor 30 > Lead Moderator 20, frontend-ia.md §3 Connect)...
        assertTrue(manageable.contains(ShellRoute.Discord))
        // ...but never the Broadcaster-floored pages (Roles, Integrations).
        assertFalse(manageable.contains(ShellRoute.PeopleRoles))
        assertFalse(manageable.contains(ShellRoute.Integrations))
    }

    @Test
    fun broadcaster_can_manage_every_page_that_has_a_manage_floor() {
        // The Broadcaster clears every manage floor; the only non-manageable pages are the read-only ones
        // (Dashboard, Analytics, Settings — manage floor null), which nobody "manages" from the sidebar.
        val expected: Set<ShellRoute> =
            ShellNav.pages.filter { it.manageFloor != null }.map { it.route }.toSet()
        val manageable: Set<ShellRoute> =
            ShellNav.pages
                .filter { ShellNav.canManage(ManagementRole.Broadcaster, it.route) }
                .map { it.route }
                .toSet()

        assertEquals(expected, manageable)
        // Sanity: a read-only page is never "manageable", even for the Broadcaster.
        assertFalse(ShellNav.canManage(ManagementRole.Broadcaster, ShellRoute.Home))
        assertFalse(ShellNav.canManage(ManagementRole.Broadcaster, ShellRoute.Analytics))
    }

    // ── Held-key visibility (frontend-ia.md §3a) — a broadcaster-LOWERED page surfaces to a role-less caller ────

    @Test
    fun a_role_less_caller_holding_a_pages_read_key_sees_that_page_and_enters_the_management_shell() {
        // The broadcaster lowered `commands:read` to a VIP: the caller has NO management role, but holds the key,
        // so the Commands page becomes visible AND the shell's rung fork admits them to the management shell (where
        // every write still defaults off — read-only — until a write key is also held).
        val held: Set<String> = setOf("commands:read")

        val visible: List<NavPage> = ShellNav.visiblePagesFor(null, held)
        assertTrue(visible.any { it.route == ShellRoute.Commands }, "a lowered commands:read must surface Commands")
        // ONLY the held page surfaces — one lowered key never leaks the rest of the management shell.
        assertEquals(setOf(ShellRoute.Commands), visible.map { it.route }.toSet())
        assertTrue(
            ShellNav.hasManagementAccess(null, held),
            "holding a management page's read key must admit a role-less caller to the management shell",
        )
    }

    @Test
    fun a_participant_only_held_key_set_hides_every_management_page_and_stays_on_the_participant_rung() {
        // A pure participant's held keys carry only participant capabilities (e.g. self-service transfer) — none is
        // a management page's read key — so no management page is visible and the fork keeps them on the
        // participant rung. An empty held set behaves identically (the fail-closed default).
        val participantOnly: Set<String> = setOf("economy:transfer:write")

        assertTrue(ShellNav.visiblePagesFor(null, participantOnly).isEmpty())
        assertFalse(ShellNav.hasManagementAccess(null, participantOnly))
        assertTrue(ShellNav.visiblePagesFor(null, emptySet()).isEmpty())
        assertFalse(ShellNav.hasManagementAccess(null, emptySet()))
    }

    @Test
    fun a_role_that_clears_the_floor_is_unchanged_by_held_keys() {
        // The invariant that keeps the Mod/Editor/Broadcaster shells UNCHANGED: for a role that already clears the
        // floors, the visible set is identical whether or not held keys are supplied. Held keys only ADD pages a
        // role can't reach, and every keyed page floors at Moderator (Broadcaster pages carry a null key), so a
        // Mod's real held set never surfaces anything the role branch didn't already.
        val roleOnly: Set<ShellRoute> =
            ShellNav.visiblePagesFor(ManagementRole.Moderator).map { it.route }.toSet()
        val withHeldReadKeys: Set<ShellRoute> =
            ShellNav.visiblePagesFor(
                    ManagementRole.Moderator,
                    setOf("commands:read", "quotes:read", "chat:read", "reward:read"),
                )
                .map { it.route }
                .toSet()

        assertEquals(roleOnly, withHeldReadKeys)
        // Every management role always enters the management shell, held keys or not.
        ManagementRole.entries.forEach { role ->
            assertTrue(ShellNav.hasManagementAccess(role, emptySet()), "$role must always reach the management shell")
        }
    }

    // ── Chat/Stream reorg (frontend-ia.md §3/§8, owner punch list 2026-09-08 §9) ───────────────────────────
    //
    // Building one command with a custom overlay/script used to require jumping between the Chat group
    // (Commands/Pipelines) and a separate Stream group (Overlays/Code Scripts). These assertions prove the
    // actual structure that fixes it: Overlays, Code Scripts and Pipelines render under the SAME group as
    // Commands, and Stream sheds them without losing its own remaining pages or role floors.

    @Test
    fun overlays_and_code_scripts_and_pipelines_share_the_commands_group() {
        val commandsGroup: NavGroup = ShellNav.pages.first { it.route == ShellRoute.Commands }.group

        assertEquals(NavGroup.Build, commandsGroup)
        assertEquals(commandsGroup, ShellNav.pages.first { it.route == ShellRoute.OverlaysGallery }.group)
        assertEquals(commandsGroup, ShellNav.pages.first { it.route == ShellRoute.OverlaysMine }.group)
        assertEquals(commandsGroup, ShellNav.pages.first { it.route == ShellRoute.Scripts }.group)
        assertEquals(commandsGroup, ShellNav.pages.first { it.route == ShellRoute.Pipelines }.group)
    }

    @Test
    fun the_build_group_shows_gallery_with_both_tabs_to_a_moderator_and_setup_holds_no_bundles() {
        val moderator: List<NavPage> = ShellNav.visiblePagesFor(ManagementRole.Moderator)

        listOf(ShellRoute.GalleryTemplates, ShellRoute.GalleryBundles).forEach { route ->
            val page: NavPage = moderator.first { it.route == route }
            assertEquals(NavGroup.Build, page.group, "$route sits in Build")
            assertTrue(ShellNav.canManage(ManagementRole.Moderator, route), "Moderator manages $route")
        }
        val setupRoutes: List<ShellRoute> = ShellNav.pages.filter { it.group == NavGroup.Setup }.map { it.route }
        assertFalse(setupRoutes.contains(ShellRoute.GalleryBundles))
        assertFalse(setupRoutes.contains(ShellRoute.GalleryTemplates))
    }

    @Test
    fun live_group_holds_only_the_broadcast_facing_pages() {
        val liveRoutes: Set<ShellRoute> = ShellNav.pages.filter { it.group == NavGroup.Live }.map { it.route }.toSet()

        // Live is what you use while the stream runs. The build-workflow tools sit in Build, and Alerts is an
        // event-response tab, so none of them is in Live.
        assertEquals(
            setOf(
                ShellRoute.ChatChannel,
                ShellRoute.ChatAll,
                ShellRoute.StreamInfo,
                ShellRoute.StreamInteractions,
                ShellRoute.StreamSchedule,
                ShellRoute.ObsLiveControl,
                ShellRoute.ObsAdvanced,
                ShellRoute.VtubeStudio,
                ShellRoute.Analytics,
            ),
            liveRoutes,
        )
        assertFalse(liveRoutes.contains(ShellRoute.OverlaysGallery))
        assertFalse(liveRoutes.contains(ShellRoute.OverlaysMine))
        assertFalse(liveRoutes.contains(ShellRoute.Scripts))
        assertFalse(liveRoutes.contains(ShellRoute.Pipelines))
    }

    @Test
    fun moving_overlays_and_code_scripts_into_chat_does_not_change_their_role_floors() {
        // A regrouping must not silently relax or tighten who can see/mutate a page — only WHERE it renders
        // changes. Overlays keeps its Moderator-read/Editor-manage floor; Code Scripts keeps its
        // Broadcaster-only floor (a sandboxed code editor is not a Moderator-grade surface just because it
        // now sits beside Commands).
        val widgetPages: List<NavPage> =
            ShellNav.pages.filter { it.route == ShellRoute.OverlaysGallery || it.route == ShellRoute.OverlaysMine }
        val codeScripts: NavPage = ShellNav.pages.first { it.route == ShellRoute.Scripts }

        assertEquals(2, widgetPages.size)
        widgetPages.forEach { widgets ->
            assertEquals(ManagementRole.Moderator, widgets.readFloor)
            assertEquals(ManagementRole.Editor, widgets.manageFloor)
        }
        // S-IA-MOD-FLOORS: the server floors code:script:author at Moderator, so the app shows it to a Moderator.
        assertEquals(ManagementRole.Moderator, codeScripts.readFloor)
        assertEquals(ManagementRole.Moderator, codeScripts.manageFloor)

        val moderatorVisible: Set<ShellRoute> = ShellNav.visiblePagesFor(ManagementRole.Moderator).map { it.route }.toSet()
        assertTrue(moderatorVisible.contains(ShellRoute.Scripts), "a Moderator clears code:script:author")
        val editorVisible: Set<ShellRoute> = ShellNav.visiblePagesFor(ManagementRole.Editor).map { it.route }.toSet()
        assertTrue(editorVisible.contains(ShellRoute.OverlaysGallery), "an Editor must still see the Overlays Gallery")
        assertTrue(editorVisible.contains(ShellRoute.OverlaysMine), "an Editor must still see My overlays")
        assertTrue(editorVisible.contains(ShellRoute.Scripts))
    }

    @Test
    fun moderator_manages_the_stream_and_channel_pages_the_server_floors_at_moderator() {
        // Mirrors the 24 actions S-IA-MOD-FLOORS moved to the Moderator floor on the server.
        listOf(
            ShellRoute.Scripts,
            ShellRoute.Economy,
            ShellRoute.Games,
            ShellRoute.StreamSchedule,
            ShellRoute.GalleryBundles,
            ShellRoute.MusicNowPlaying,
        ).forEach { route ->
            assertTrue(ShellNav.canManage(ManagementRole.Moderator, route), "Moderator manages $route")
        }
        assertTrue(ShellNav.canManage(ManagementRole.Moderator, ShellRoute.Economy, ManageAction.EconomyPayoutRules))
        assertTrue(ShellNav.canManage(ManagementRole.Moderator, ShellRoute.MusicNowPlaying, ManageAction.MusicConfig))
    }

    @Test
    fun an_editor_can_manage_overlays_and_a_moderator_can_manage_code_scripts() {
        // canManage is the actual consequence a screen renders (button enabled vs. disabled-with-reason) —
        // assert it, not just the raw manageFloor field, so a regression in canManage's own logic would fail
        // this test even if the floors above stayed correct.
        assertTrue(ShellNav.canManage(ManagementRole.Editor, ShellRoute.OverlaysMine))
        assertFalse(ShellNav.canManage(ManagementRole.Moderator, ShellRoute.OverlaysMine), "Moderator is below Overlays' Editor manage floor")
        assertTrue(ShellNav.canManage(ManagementRole.Moderator, ShellRoute.Scripts))
        assertFalse(ShellNav.canManage(null, ShellRoute.Scripts))
    }

    @Test
    fun the_wire_role_supermod_is_labelled_lead_moderator_and_the_rung_is_named_lead_moderator() {
        assertEquals(
            Res.string.shell_role_lead_moderator,
            channelRoleLabelResource("supermod"),
        )
        assertEquals(20, ManagementRole.valueOf("LeadModerator").level)
        assertEquals(
            listOf("Moderator", "LeadModerator", "Editor", "Broadcaster"),
            ManagementRole.entries.map { it.name },
        )
    }

    @Test
    fun a_moderator_sees_the_tts_live_voices_and_rules_tabs_but_not_provider() {
        val moderator: Set<ShellRoute> =
            ShellNav.visiblePagesFor(ManagementRole.Moderator).map { it.route }.toSet()

        assertTrue(moderator.containsAll(setOf(ShellRoute.TtsLive, ShellRoute.TtsVoices, ShellRoute.TtsRules)))
        assertFalse(ShellRoute.TtsProvider in moderator)
    }

    @Test
    fun an_editor_sees_all_four_tts_tabs() {
        val editor: Set<ShellRoute> = ShellNav.visiblePagesFor(ManagementRole.Editor).map { it.route }.toSet()

        assertTrue(
            editor.containsAll(
                setOf(ShellRoute.TtsLive, ShellRoute.TtsVoices, ShellRoute.TtsRules, ShellRoute.TtsProvider),
            ),
        )
    }
}
