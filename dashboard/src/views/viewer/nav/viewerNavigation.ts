// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------
import type { Component } from 'vue';
import ChatMessages from '~icons/nnz/chat-messages';
import Dices from '~icons/nnz/dices';
import Gem from '~icons/nnz/gem';
import Home from '~icons/nnz/home';
import NoteEighthPair from '~icons/nnz/note-eighth-pair';
import Trophy from '~icons/nnz/trophy';
import User from '~icons/nnz/user';

export type ViewerNavEntry = {
  // The route name of the page; every viewer page takes the :channel param.
  route: string;
  // Words key under frame.nav.
  label: string;
  // The one-word label of the phone bar, where seven pages share 390 px and a label may not be cut.
  shortLabel: string;
  icon: Component;
};

// The one viewer navigation: seven pages, each under /me/<channel>.
export const viewerNavEntries = [
  {
    route: 'viewer-me',
    label: 'viewer.myChannel',
    shortLabel: 'home',
    icon: Home,
  },
  {
    route: 'viewer-chat',
    label: 'viewer.chat',
    shortLabel: 'viewer.chat',
    icon: ChatMessages,
  },
  {
    route: 'viewer-now-playing',
    label: 'viewer.nowPlaying',
    shortLabel: 'music',
    icon: NoteEighthPair,
  },
  {
    route: 'viewer-leaderboards',
    label: 'viewer.leaderboards',
    shortLabel: 'viewer.ranks',
    icon: Trophy,
  },
  {
    route: 'viewer-points-store',
    label: 'viewer.pointsStore',
    shortLabel: 'viewer.store',
    icon: Gem,
  },
  {
    route: 'viewer-games',
    label: 'viewer.games',
    shortLabel: 'viewer.games',
    icon: Dices,
  },
  {
    route: 'viewer-account',
    label: 'viewer.me',
    shortLabel: 'viewer.me',
    icon: User,
  },
] as const satisfies readonly ViewerNavEntry[];
