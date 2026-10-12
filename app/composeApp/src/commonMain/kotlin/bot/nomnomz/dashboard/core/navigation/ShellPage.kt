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

// The page registry of the dashboard: one entry per page, with its address slug and the slugs of its tabs, in tab
// order. A page slug alone opens the first tab. Keep one entry per line, slug first, tabs as string literals on the
// same line: scripts/check-ia-placement.py reads this file to keep it in step with the UI placement ledger.
// Tab labels and page titles come from string resources (shell_tab_<page>_<tab>), never from this file.
enum class ShellPage(val slug: String, val tabs: List<String>) {
    Home("home", emptyList()),
    Chat("chat", listOf("one-channel", "all-my-channels")),
    Commands("commands", emptyList()),
    TriggersTimers("triggers-timers", listOf("chat-triggers", "voice-triggers", "timers")),
    EventResponses("event-responses", listOf("responses", "alert-queue")),
    Library("library", listOf("sounds", "files", "random-responses", "quotes")),
    Pipelines("pipelines", emptyList()),
    Overlays("overlays", listOf("gallery", "my-overlays")),
    Scripts("scripts", emptyList()),
    People("people", listOf("overview", "queue", "viewers", "history", "rules", "roles")),
    ChannelPoints("channel-points", emptyList()),
    Economy("economy", emptyList()),
    Games("games", emptyList()),
    Giveaways("giveaways", emptyList()),
    Music("music", listOf("now-playing", "song-requests")),
    Tts("tts", listOf("live", "voices", "rules", "provider")),
    MediaShare("media-share", listOf("queue", "settings")),
    Stream("stream", listOf("info-presets", "interactions", "schedule")),
    Analytics("analytics", emptyList()),
    Obs("obs", listOf("live-control", "advanced")),
    VtubeStudio("vtube-studio", listOf("hotkeys", "expressions")),
    Discord("discord", emptyList()),
    Webhooks("webhooks", emptyList()),
    Federation("federation", emptyList()),
    CustomEvents("custom-events", emptyList()),
    Supporters("supporters", emptyList()),
    ApiDevices("api-devices", emptyList()),
    Gallery("gallery", listOf("templates", "bundles")),
    Integrations("integrations", emptyList()),
    Features("features", emptyList()),
    Settings("settings", emptyList()),
    MyData("my-data", emptyList()),
    Admin("admin", emptyList()),
}
