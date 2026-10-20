// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
