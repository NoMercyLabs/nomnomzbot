// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.navigation

import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

// Proves the page/tab address contract the web reload depends on (RouteStore). If a slug collided or stopped
// round-tripping, a reload would silently land the operator on the wrong page, so these are the behaviours, not
// the surface. Iterating ShellRoute.entries keeps the proof exhaustive as routes are added.
class ShellRouteSlugTest {

    @Test
    fun every_route_round_trips_through_its_slug() {
        ShellRoute.entries.forEach { route ->
            val slug: String = ShellRouteSlug.of(route)
            assertTrue(slug.isNotBlank(), "blank slug for $route")
            assertEquals(route, ShellRouteSlug.parse(slug), "slug '$slug' did not round-trip to $route")
        }
    }

    @Test
    fun slugs_are_unique_per_route() {
        val slugs: List<String> = ShellRoute.entries.map { ShellRouteSlug.of(it) }

        assertEquals(slugs.size, slugs.toSet().size, "duplicate slugs would make a URL ambiguous: $slugs")
    }

    @Test
    fun a_page_with_tabs_always_writes_its_tab_into_the_slug() {
        assertEquals("chat/one-channel", ShellRouteSlug.of(ShellRoute.ChatChannel))
        assertEquals("chat/all-my-channels", ShellRouteSlug.of(ShellRoute.ChatAll))
        assertEquals("people/queue", ShellRouteSlug.of(ShellRoute.PeopleQueue))
        assertEquals("library/random-responses", ShellRouteSlug.of(ShellRoute.LibraryRandomResponses))
        assertEquals("event-responses/alert-queue", ShellRouteSlug.of(ShellRoute.EventResponsesAlertQueue))
        assertEquals("gallery/templates", ShellRouteSlug.of(ShellRoute.GalleryTemplates))
        assertEquals("gallery/bundles", ShellRouteSlug.of(ShellRoute.GalleryBundles))
        assertEquals("music/song-requests", ShellRouteSlug.of(ShellRoute.MusicSongRequests))
        assertEquals(ShellRoute.MusicSongRequests, ShellRouteSlug.parse("music/song-requests"))
    }

    @Test
    fun the_gallery_page_slug_opens_its_templates_tab_and_the_old_bundles_address_lands_on_home() {
        assertEquals(ShellRoute.GalleryTemplates, ShellRouteSlug.parse("gallery"))
        assertEquals(ShellRoute.GalleryBundles, ShellRouteSlug.parse("gallery/bundles"))
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("bundles"))
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("setup/bundles"))
    }

    @Test
    fun the_four_tts_tabs_resolve_and_the_bare_tts_address_opens_live() {
        assertEquals(ShellRoute.TtsLive, ShellRouteSlug.parse("tts/live"))
        assertEquals(ShellRoute.TtsVoices, ShellRouteSlug.parse("tts/voices"))
        assertEquals(ShellRoute.TtsRules, ShellRouteSlug.parse("tts/rules"))
        assertEquals(ShellRoute.TtsProvider, ShellRouteSlug.parse("tts/provider"))
        assertEquals("tts/provider", ShellRouteSlug.of(ShellRoute.TtsProvider))
        assertEquals(ShellRoute.TtsLive, ShellRouteSlug.parse("tts"))
    }

    @Test
    fun the_media_share_page_has_queue_and_settings_tabs_and_the_page_slug_opens_queue() {
        assertEquals(ShellRoute.MediaShareQueue, ShellRouteSlug.parse("media-share/queue"))
        assertEquals(ShellRoute.MediaShareSettings, ShellRouteSlug.parse("media-share/settings"))
        assertEquals(ShellRoute.MediaShareQueue, ShellRouteSlug.parse("media-share"))
        assertEquals("media-share/queue", ShellRouteSlug.of(ShellRoute.MediaShareQueue))
        assertEquals("media-share/settings", ShellRouteSlug.of(ShellRoute.MediaShareSettings))
        assertEquals(
            listOf(ShellRoute.MediaShareQueue, ShellRoute.MediaShareSettings),
            ShellRoute.tabsOf(ShellPage.MediaShare),
        )
    }

    @Test
    fun a_page_without_tabs_is_its_page_slug_alone() {
        assertEquals("home", ShellRouteSlug.of(ShellRoute.Home))
        assertEquals("commands", ShellRouteSlug.of(ShellRoute.Commands))
        assertEquals("api-devices", ShellRouteSlug.of(ShellRoute.ApiDevices))
        assertEquals("vtube-studio", ShellRouteSlug.of(ShellRoute.VtubeStudio))
    }

    @Test
    fun a_page_slug_alone_opens_the_first_tab_of_that_page() {
        assertEquals(ShellRoute.ChatChannel, ShellRouteSlug.parse("chat"))
        assertEquals(ShellRoute.PeopleOverview, ShellRouteSlug.parse("people"))
        assertEquals(ShellRoute.LibrarySounds, ShellRouteSlug.parse("library"))
        assertEquals(ShellRoute.MusicNowPlaying, ShellRouteSlug.parse("music"))
    }

    @Test
    fun an_empty_slug_falls_back_to_home() {
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse(""))
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("   "))
    }

    @Test
    fun an_unknown_page_or_tab_falls_back_to_home() {
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("not-a-real-page"))
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("chat/not-a-real-tab"))
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("commands/extra"))
    }

    @Test
    fun an_address_from_before_the_new_layout_lands_on_home() {
        // V0: the old one-word slugs are not mapped to the new places; they are unknown addresses.
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("songrequests"))
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("multichat"))
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("moderationqueue"))
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("dashboard"))
        assertNull(ShellRouteSlug.find("songrequests"))
    }

    @Test
    fun the_old_alerts_tab_address_lands_on_home() {
        // The Alerts tab became the alert-queue tab; the old address is an unknown one (no alias).
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("event-responses/alerts"))
        assertNull(ShellRouteSlug.find("event-responses/alerts"))
    }

    @Test
    fun the_stream_page_has_three_tabs_and_opens_on_info_and_presets() {
        assertEquals(listOf("info-presets", "interactions", "schedule"), ShellPage.Stream.tabs)
        assertEquals(ShellRoute.StreamInfo, ShellRouteSlug.parse("stream"))
        assertEquals(ShellRoute.StreamInfo, ShellRouteSlug.parse("stream/info-presets"))
        assertEquals(ShellRoute.StreamInteractions, ShellRouteSlug.parse("stream/interactions"))
        assertEquals(ShellRoute.StreamSchedule, ShellRouteSlug.parse("stream/schedule"))
        assertEquals("stream/info-presets", ShellRouteSlug.of(ShellRoute.StreamInfo))
        assertEquals(
            listOf(ShellRoute.StreamInfo, ShellRoute.StreamInteractions, ShellRoute.StreamSchedule),
            ShellRoute.tabsOf(ShellPage.Stream),
        )
    }

    @Test
    fun the_old_stream_info_and_poll_addresses_land_on_home() {
        // The stream info editor left Settings and the chat polls card left Home; neither had its own address,
        // and no alias maps an old guess to the new tabs.
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("stream/info"))
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("stream/polls"))
        assertEquals(ShellRoute.Home, ShellRouteSlug.parse("chat-polls"))
        assertNull(ShellRouteSlug.find("stream/info"))
    }

    @Test
    fun parsing_ignores_surrounding_whitespace_and_case() {
        val route: ShellRoute = ShellRoute.PeopleHistory
        val canonical: String = ShellRouteSlug.of(route)

        assertEquals(route, ShellRouteSlug.parse(canonical.uppercase()))
        assertEquals(route, ShellRouteSlug.parse("  $canonical  "))
    }

    @Test
    fun find_returns_null_for_an_unknown_slug_and_the_route_for_a_known_one() {
        assertNull(ShellRouteSlug.find("nope"))
        assertEquals(ShellRoute.TriggersTimers, ShellRouteSlug.find("triggers-timers/timers"))
    }
}
