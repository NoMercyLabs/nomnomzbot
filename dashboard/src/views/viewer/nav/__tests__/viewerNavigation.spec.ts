// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
import { createMemoryHistory, createRouter } from 'vue-router';
import { describe, expect, it } from 'vitest';

import { viewerRoutes } from '@/router/viewer';

import { viewerNavEntries } from '../viewerNavigation';

function viewerRouter() {
  return createRouter({ history: createMemoryHistory(), routes: [viewerRoutes] });
}

describe('the viewer navigation', () => {
  it('lists the seven pages in order', () => {
    expect(viewerNavEntries.map((entry) => entry.label)).toEqual([
      'viewer.myChannel',
      'viewer.chat',
      'viewer.nowPlaying',
      'viewer.leaderboards',
      'viewer.pointsStore',
      'viewer.games',
      'viewer.me',
    ]);
  });

  it('resolves every entry to an address under /me/<channel>', () => {
    const router = viewerRouter();
    const paths = viewerNavEntries.map(
      (entry) => router.resolve({ name: entry.route, params: { channel: 'stoney_eagle' } }).path,
    );

    expect(paths).toEqual([
      '/me/stoney_eagle',
      '/me/stoney_eagle/chat',
      '/me/stoney_eagle/now-playing',
      '/me/stoney_eagle/leaderboards',
      '/me/stoney_eagle/points-store',
      '/me/stoney_eagle/games',
      '/me/stoney_eagle/me',
    ]);
  });

  it('opens every entry for a signed-in person without an action key', () => {
    const router = viewerRouter();

    for (const entry of viewerNavEntries) {
      const meta = router.resolve({ name: entry.route, params: { channel: 'x' } }).meta;

      expect(meta.navMode).toBeUndefined();
    }
  });
});
