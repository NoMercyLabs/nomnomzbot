// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import {
  AudioLines,
  ChartLine,
  Clapperboard,
  Code,
  Dices,
  Feather,
  Gem,
  Gift,
  HeartHandshake,
  House,
  Layers,
  LayoutGrid,
  Library,
  MessagesSquare,
  Music,
  Radio,
  Settings,
  Smile,
  Terminal,
  Timer,
  Tv,
  Users,
  Workflow,
  Zap,
} from '@lucide/vue'
import type { Component } from 'vue'

export type NavEntry = {
  // The route name of the page. An entry shows only when this route exists.
  route: string
  // Words key under frame.nav.
  label: string
  icon: Component
  // One of the four entries the phone bottom bar keeps in reach.
  bottom?: boolean
}

export type NavGroup = {
  key: string
  entries: readonly NavEntry[]
}

// The streamer dashboard groups, from the page list (spec frontend-vue-components.md section 4; mockup base/shell.js NAV).
export const dashboardNav: readonly NavGroup[] = [
  { key: 'home', entries: [{ route: 'dashboard-home', label: 'home', icon: House, bottom: true }] },
  {
    key: 'live',
    entries: [
      { route: 'dashboard-chat', label: 'chat', icon: MessagesSquare, bottom: true },
      { route: 'dashboard-stream', label: 'stream', icon: Radio, bottom: true },
      { route: 'dashboard-analytics', label: 'analytics', icon: ChartLine },
      { route: 'dashboard-obs', label: 'obs', icon: Clapperboard },
      { route: 'dashboard-vtube-studio', label: 'vtubeStudio', icon: Smile },
    ],
  },
  {
    key: 'build',
    entries: [
      { route: 'dashboard-commands', label: 'commands', icon: Terminal, bottom: true },
      { route: 'dashboard-triggers-timers', label: 'triggersTimers', icon: Timer },
      { route: 'dashboard-event-responses', label: 'eventResponses', icon: Zap },
      { route: 'dashboard-pipelines', label: 'pipelines', icon: Workflow },
      { route: 'dashboard-overlays', label: 'overlays', icon: Layers },
      { route: 'dashboard-scripts', label: 'scripts', icon: Code },
      { route: 'dashboard-library', label: 'library', icon: Library },
      { route: 'dashboard-gallery', label: 'gallery', icon: LayoutGrid },
    ],
  },
  { key: 'people', entries: [{ route: 'dashboard-people', label: 'people', icon: Users }] },
  {
    key: 'loyalty',
    entries: [
      { route: 'dashboard-channel-points', label: 'channelPoints', icon: Gem },
      { route: 'dashboard-economy', label: 'economy', icon: Feather },
      { route: 'dashboard-games', label: 'games', icon: Dices },
      { route: 'dashboard-giveaways', label: 'giveaways', icon: Gift },
      { route: 'dashboard-supporters', label: 'supporters', icon: HeartHandshake },
    ],
  },
  {
    key: 'musicVoice',
    entries: [
      { route: 'dashboard-music', label: 'music', icon: Music },
      { route: 'dashboard-tts', label: 'tts', icon: AudioLines },
      { route: 'dashboard-media-share', label: 'mediaShare', icon: Tv },
    ],
  },
  { key: 'setup', entries: [{ route: 'dashboard-settings', label: 'settings', icon: Settings }] },
]
