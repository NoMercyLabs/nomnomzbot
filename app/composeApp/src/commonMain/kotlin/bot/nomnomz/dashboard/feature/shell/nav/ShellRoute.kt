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

/**
 * A destination of the dashboard: a [page] and, when the page has tabs, the [tab] slug. Every tab of every page has
 * exactly one entry here, and every page without tabs has exactly one (proven in ShellRouteCatalogTest).
 */
enum class ShellRoute(val page: ShellPage, val tab: String?) {
    Home(ShellPage.Home, null),
    ChatChannel(ShellPage.Chat, "one-channel"),
    ChatAll(ShellPage.Chat, "all-my-channels"),
    Commands(ShellPage.Commands, null),
    TriggersChat(ShellPage.TriggersTimers, "chat-triggers"),
    TriggersVoice(ShellPage.TriggersTimers, "voice-triggers"),
    TriggersTimers(ShellPage.TriggersTimers, "timers"),
    EventResponsesResponses(ShellPage.EventResponses, "responses"),
    EventResponsesAlertQueue(ShellPage.EventResponses, "alert-queue"),
    LibrarySounds(ShellPage.Library, "sounds"),
    LibraryFiles(ShellPage.Library, "files"),
    LibraryRandomResponses(ShellPage.Library, "random-responses"),
    LibraryQuotes(ShellPage.Library, "quotes"),
    Pipelines(ShellPage.Pipelines, null),
    Overlays(ShellPage.Overlays, null),
    Scripts(ShellPage.Scripts, null),
    PeopleOverview(ShellPage.People, "overview"),
    PeopleQueue(ShellPage.People, "queue"),
    PeopleViewers(ShellPage.People, "viewers"),
    PeopleHistory(ShellPage.People, "history"),
    PeopleRules(ShellPage.People, "rules"),
    PeopleRoles(ShellPage.People, "roles"),
    ChannelPoints(ShellPage.ChannelPoints, null),
    Economy(ShellPage.Economy, null),
    Games(ShellPage.Games, null),
    Giveaways(ShellPage.Giveaways, null),
    MusicNowPlaying(ShellPage.Music, "now-playing"),
    MusicSongRequests(ShellPage.Music, "song-requests"),
    Tts(ShellPage.Tts, null),
    MediaShare(ShellPage.MediaShare, null),
    StreamSchedule(ShellPage.Stream, "schedule"),
    Analytics(ShellPage.Analytics, null),
    Obs(ShellPage.Obs, null),
    VtubeStudio(ShellPage.VtubeStudio, null),
    Discord(ShellPage.Discord, null),
    Webhooks(ShellPage.Webhooks, null),
    Federation(ShellPage.Federation, null),
    CustomEvents(ShellPage.CustomEvents, null),
    Supporters(ShellPage.Supporters, null),
    ApiDevices(ShellPage.ApiDevices, null),
    GalleryTemplates(ShellPage.Gallery, "templates"),
    GalleryBundles(ShellPage.Gallery, "bundles"),
    Integrations(ShellPage.Integrations, null),
    Features(ShellPage.Features, null),
    Settings(ShellPage.Settings, null),
    MyData(ShellPage.MyData, null),
    Admin(ShellPage.Admin, null);

    companion object {
        /** The route of [page] and [tab], or null when the page has no such tab. */
        fun of(page: ShellPage, tab: String?): ShellRoute? = entries.firstOrNull { it.page == page && it.tab == tab }

        /** The route a page opens on: its first tab, or the page itself when it has none. */
        fun defaultFor(page: ShellPage): ShellRoute = of(page, page.tabs.firstOrNull()) ?: error("no route for $page")

        /** The routes of a page's tabs, in the page's tab order. */
        fun tabsOf(page: ShellPage): List<ShellRoute> = page.tabs.mapNotNull { of(page, it) }
    }
}
