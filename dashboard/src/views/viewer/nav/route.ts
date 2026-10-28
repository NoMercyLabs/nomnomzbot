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
import type { RouteRecordRaw } from 'vue-router';

// The viewer pages that are planned and not built yet: each opens the empty "coming soon" page.
// A page that lands replaces its line here with its own route file.
function comingSoon(path: string, name: string, label: string) {
  return {
    path,
    name,
    meta: { open: true, comingSoon: label },
    component: () => import('@/views/dashboard/scaffold/ComingSoonView.vue'),
  } as const satisfies RouteRecordRaw;
}

export const viewerComingSoonRoutes: readonly RouteRecordRaw[] = [
  comingSoon(':channel/now-playing', 'viewer-now-playing', 'viewer.nowPlaying'),
  comingSoon(':channel/leaderboards', 'viewer-leaderboards', 'viewer.leaderboards'),
  comingSoon(':channel/points-store', 'viewer-points-store', 'viewer.pointsStore'),
  comingSoon(':channel/games', 'viewer-games', 'viewer.games'),
];
