// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import ChatMessages from '~icons/nnz/chat-messages';
import Dices from '~icons/nnz/dices';
import Gem from '~icons/nnz/gem';
import Home from '~icons/nnz/home';
import NoteEighthPair from '~icons/nnz/note-eighth-pair';
import Trophy from '~icons/nnz/trophy';
import User from '~icons/nnz/user';

import { backEntry, navEntry, type NavGroup } from './navigation';

// The viewer sidebar (mockup base/shell.js NAV.viewer): one group with no title.
export const viewerNav: readonly NavGroup[] = [
  {
    key: 'back',
    entries: [backEntry('back.dashboard')],
  },
  {
    key: 'viewer',
    entries: [
      navEntry('dashboard-viewer-my-channel', 'viewer.myChannel', Home),
      navEntry('dashboard-viewer-chat', 'viewer.chat', ChatMessages),
      navEntry('dashboard-viewer-now-playing', 'viewer.nowPlaying', NoteEighthPair),
      navEntry('dashboard-viewer-leaderboards', 'viewer.leaderboards', Trophy),
      navEntry('dashboard-viewer-points-store', 'viewer.pointsStore', Gem),
      navEntry('dashboard-viewer-games', 'viewer.games', Dices),
      navEntry('dashboard-viewer-me', 'viewer.me', User),
    ],
  },
];
