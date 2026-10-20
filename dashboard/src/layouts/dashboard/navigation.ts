// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Component } from 'vue';
import ArrowLeft from '~icons/nnz/arrow-left';
import BarChart from '~icons/nnz/bar-chart';
import ChartLine from '~icons/nnz/chart-positive';
import ChatMessages from '~icons/nnz/chat-messages';
import Code from '~icons/nnz/code';
import Coins from '~icons/nnz/coins';
import Dices from '~icons/nnz/dices';
import Gem from '~icons/nnz/gem';
import Gift from '~icons/nnz/gift-card';
import Grid from '~icons/nnz/grid';
import Handshake from '~icons/nnz/handshake';
import Home from '~icons/nnz/home';
import LiveStream from '~icons/nnz/live-stream';
import Mic from '~icons/nnz/mic';
import MovieClap from '~icons/nnz/movie-clap';
import NoteEighthPair from '~icons/nnz/note-eighth-pair';
import PictureInPicture from '~icons/nnz/picture-in-picture';
import Books from '~icons/nnz/books';
import Quotes from '~icons/nnz/quotes';
import Settings from '~icons/nnz/settings';
import Smile from '~icons/nnz/smile';
import SplitBranch from '~icons/nnz/split-branch';
import Terminal from '~icons/nnz/terminal';
import Timer from '~icons/nnz/timer';
import Tv from '~icons/nnz/tv';
import Users from '~icons/nnz/users';
import WaveForm from '~icons/nnz/wave-form';
import Lightning from '~icons/nnz/lightning';

import { merge } from '@/lib/array';

// Which sidebar a page belongs to. A route says it in meta.navMode; a route without it sits in the dashboard.
export type NavMode = 'dashboard' | 'admin' | 'settings';

export type NavEntry = {
  // The route name of the page. An entry shows only when this route exists.
  route: string;
  // Words key under frame.nav.
  label: string;
  icon: Component;
  // One of the four entries the phone bottom bar keeps in reach.
  bottom?: boolean;
  // Current only on its own route, not on the routes named after it (the first settings page shares a prefix with the rest).
  exact?: boolean;
  // The action key that lets a person read the page. Without it the entry shows for everyone.
  action?: string;
  // The action key that hides the entry when the person holds it (the viewer chat gives way to the operator chat).
  unless?: string;
  // The way back out of a sidebar: it never hides, and leads to the first page the person may open.
  back?: boolean;
};

export type NavGroup = {
  key: string;
  entries: readonly NavEntry[];
};

// The streamer dashboard groups, from the page list (spec frontend-vue-components.md section 4; mockup base/shell.js NAV).
export const dashboardNav: readonly NavGroup[] = [
  {
    key: 'home',
    entries: [
      {
        route: 'dashboard-home',
        label: 'home',
        icon: Home,
        action: 'dashboard:read',
        bottom: true,
      },
    ],
  },
  {
    key: 'live',
    entries: [
      {
        route: 'dashboard-chat',
        label: 'chat',
        icon: ChatMessages,
        action: 'chat:read',
        bottom: true,
      },
      {
        route: 'dashboard-viewer-chat',
        label: 'viewerChat',
        icon: ChatMessages,
        action: 'chat:viewer:read',
        unless: 'chat:read',
      },
      {
        route: 'dashboard-stream',
        label: 'stream',
        icon: LiveStream,
        action: 'stream:read',
        bottom: true,
      },
      {
        route: 'dashboard-analytics',
        label: 'analytics',
        icon: ChartLine,
        action: 'analytics:read',
      },
      {
        route: 'dashboard-polls',
        label: 'polls',
        icon: BarChart,
        action: 'live-ops:polls:read',
      },
      {
        route: 'dashboard-obs',
        label: 'obs',
        icon: MovieClap,
        action: 'obs:scene:read',
      },
      {
        route: 'dashboard-vtube-studio',
        label: 'vtubeStudio',
        icon: Smile,
        action: 'vts:config:read',
      },
    ],
  },
  {
    key: 'build',
    entries: [
      {
        route: 'dashboard-commands',
        label: 'commands',
        icon: Terminal,
        action: 'commands:read',
        bottom: true,
      },
      {
        route: 'dashboard-triggers-timers',
        label: 'triggersTimers',
        icon: Timer,
        action: 'timers:read',
      },
      {
        route: 'dashboard-voice-triggers',
        label: 'voiceTriggers',
        icon: Mic,
        action: 'voicetriggers:read',
      },
      {
        route: 'dashboard-event-responses',
        label: 'eventResponses',
        icon: Lightning,
        action: 'eventresponses:read',
      },
      {
        route: 'dashboard-pipelines',
        label: 'pipelines',
        icon: SplitBranch,
        action: 'pipelines:read',
      },
      {
        route: 'dashboard-overlays',
        label: 'overlays',
        icon: PictureInPicture,
        action: 'widget:read',
      },
      {
        route: 'dashboard-scripts',
        label: 'scripts',
        icon: Code,
        action: 'code:script:author',
      },
      {
        route: 'dashboard-library',
        label: 'library',
        icon: Books,
        action: 'bundles:read',
      },
      {
        route: 'dashboard-gallery',
        label: 'gallery',
        icon: Grid,
        action: 'widget:read',
      },
    ],
  },
  {
    key: 'people',
    entries: [
      {
        route: 'dashboard-people',
        label: 'people',
        icon: Users,
        action: 'community:read',
      },
    ],
  },
  {
    key: 'loyalty',
    entries: [
      {
        route: 'dashboard-channel-points',
        label: 'channelPoints',
        icon: Gem,
        action: 'reward:read',
      },
      {
        route: 'dashboard-economy',
        label: 'economy',
        icon: Coins,
        action: 'economy:catalog:read',
      },
      {
        route: 'dashboard-games',
        label: 'games',
        icon: Dices,
        action: 'games:session:read',
      },
      {
        route: 'dashboard-giveaways',
        label: 'giveaways',
        icon: Gift,
        action: 'giveaways:read',
      },
      {
        route: 'dashboard-quotes',
        label: 'quotes',
        icon: Quotes,
        action: 'quotes:read',
      },
      {
        route: 'dashboard-supporters',
        label: 'supporters',
        icon: Handshake,
        action: 'supporters:read',
      },
    ],
  },
  {
    key: 'musicVoice',
    entries: [
      {
        route: 'dashboard-music',
        label: 'music',
        icon: NoteEighthPair,
        action: 'music:config:read',
      },
      {
        route: 'dashboard-tts',
        label: 'tts',
        icon: WaveForm,
        action: 'tts:config:read',
      },
      {
        route: 'dashboard-media-share',
        label: 'mediaShare',
        icon: Tv,
        action: 'media:read',
      },
    ],
  },
  {
    key: 'setup',
    entries: [
      {
        route: 'dashboard-settings',
        label: 'settings',
        icon: Settings,
        action: 'setup:write',
      },
    ],
  },
];

// A short way to write an entry for the sidebars that are not the streamer dashboard.
export function navEntry(route: string, label: string, icon: Component, exact = false) {
  return {
    route,
    label,
    icon,
    exact,
  } as const satisfies NavEntry;
}

// The way back out of the settings, admin and viewer sidebars. It names the dashboard home, but it never hides:
// a previewed viewer cannot open the home page, so the link leads to the first page that role can open.
export function backEntry(label: 'back.admin' | 'back.dashboard') {
  return {
    ...navEntry('dashboard-home', label, ArrowLeft),
    back: true,
  } as const satisfies NavEntry;
}

// The order a person lands in: home, then the viewer chat, then the rest of the dashboard sidebar as it stands.
export function landingOrder(): readonly NavEntry[] {
  const entries = dashboardNav.flatMap((group) => group.entries);
  const first = ['dashboard-home', 'dashboard-viewer-chat'];
  const preferred = first.flatMap((route) => entries.filter((entry) => entry.route === route));
  const rest = entries.filter((entry) => !first.includes(entry.route));

  return merge(preferred, rest);
}
