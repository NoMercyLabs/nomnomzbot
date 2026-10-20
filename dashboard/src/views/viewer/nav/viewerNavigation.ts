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
  icon: Component;
};

// The one viewer navigation: seven pages, each under /me/<channel>.
export const viewerNavEntries = [
  {
    route: 'viewer-me',
    label: 'viewer.myChannel',
    icon: Home,
  },
  {
    route: 'viewer-chat',
    label: 'viewer.chat',
    icon: ChatMessages,
  },
  {
    route: 'viewer-now-playing',
    label: 'viewer.nowPlaying',
    icon: NoteEighthPair,
  },
  {
    route: 'viewer-leaderboards',
    label: 'viewer.leaderboards',
    icon: Trophy,
  },
  {
    route: 'viewer-points-store',
    label: 'viewer.pointsStore',
    icon: Gem,
  },
  {
    route: 'viewer-games',
    label: 'viewer.games',
    icon: Dices,
  },
  {
    route: 'viewer-account',
    label: 'viewer.me',
    icon: User,
  },
] as const satisfies readonly ViewerNavEntry[];
