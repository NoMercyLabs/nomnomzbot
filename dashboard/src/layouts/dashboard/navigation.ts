// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Component } from 'vue'
import ChartLine from '~icons/nnz/chart-positive'
import ChatMessages from '~icons/nnz/chat-messages'
import Code from '~icons/nnz/code'
import Coins from '~icons/nnz/coins'
import Dices from '~icons/nnz/dices'
import Gem from '~icons/nnz/gem'
import Gift from '~icons/nnz/gift-card'
import Grid from '~icons/nnz/grid'
import Handshake from '~icons/nnz/handshake'
import Home from '~icons/nnz/home'
import LiveStream from '~icons/nnz/live-stream'
import MovieClap from '~icons/nnz/movie-clap'
import NoteEighthPair from '~icons/nnz/note-eighth-pair'
import PictureInPicture from '~icons/nnz/picture-in-picture'
import Books from '~icons/nnz/books'
import Settings from '~icons/nnz/settings'
import Smile from '~icons/nnz/smile'
import SplitBranch from '~icons/nnz/split-branch'
import Terminal from '~icons/nnz/terminal'
import Timer from '~icons/nnz/timer'
import Tv from '~icons/nnz/tv'
import Users from '~icons/nnz/users'
import WaveForm from '~icons/nnz/wave-form'
import Lightning from '~icons/nnz/lightning'

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
  { key: 'home', entries: [{ route: 'dashboard-home', label: 'home', icon: Home, bottom: true }] },
  {
    key: 'live',
    entries: [
      { route: 'dashboard-chat', label: 'chat', icon: ChatMessages, bottom: true },
      { route: 'dashboard-stream', label: 'stream', icon: LiveStream, bottom: true },
      { route: 'dashboard-analytics', label: 'analytics', icon: ChartLine },
      { route: 'dashboard-obs', label: 'obs', icon: MovieClap },
      { route: 'dashboard-vtube-studio', label: 'vtubeStudio', icon: Smile },
    ],
  },
  {
    key: 'build',
    entries: [
      { route: 'dashboard-commands', label: 'commands', icon: Terminal, bottom: true },
      { route: 'dashboard-triggers-timers', label: 'triggersTimers', icon: Timer },
      { route: 'dashboard-event-responses', label: 'eventResponses', icon: Lightning },
      { route: 'dashboard-pipelines', label: 'pipelines', icon: SplitBranch },
      { route: 'dashboard-overlays', label: 'overlays', icon: PictureInPicture },
      { route: 'dashboard-scripts', label: 'scripts', icon: Code },
      { route: 'dashboard-library', label: 'library', icon: Books },
      { route: 'dashboard-gallery', label: 'gallery', icon: Grid },
    ],
  },
  { key: 'people', entries: [{ route: 'dashboard-people', label: 'people', icon: Users }] },
  {
    key: 'loyalty',
    entries: [
      { route: 'dashboard-channel-points', label: 'channelPoints', icon: Gem },
      { route: 'dashboard-economy', label: 'economy', icon: Coins },
      { route: 'dashboard-games', label: 'games', icon: Dices },
      { route: 'dashboard-giveaways', label: 'giveaways', icon: Gift },
      { route: 'dashboard-supporters', label: 'supporters', icon: Handshake },
    ],
  },
  {
    key: 'musicVoice',
    entries: [
      { route: 'dashboard-music', label: 'music', icon: NoteEighthPair },
      { route: 'dashboard-tts', label: 'tts', icon: WaveForm },
      { route: 'dashboard-media-share', label: 'mediaShare', icon: Tv },
    ],
  },
  { key: 'setup', entries: [{ route: 'dashboard-settings', label: 'settings', icon: Settings }] },
]
