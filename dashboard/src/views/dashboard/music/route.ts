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

import { loadPage } from '@/i18n';

export const dashboardMusicRoute = {
  path: 'music',
  name: 'dashboard-music',
  meta: { action: 'music:config:read' },
  component: () =>
    loadPage(
      () => import('./MusicView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
      // The Music Player setup dialog on a waiting queue row speaks with the Overlays page's words.
      (locale) => import(`../overlays/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;
