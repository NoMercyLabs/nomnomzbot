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

export const dashboardObsRoute = {
  path: 'obs',
  name: 'dashboard-obs',
  meta: { action: 'obs:scene:read' },
  component: () =>
    loadPage(
      () => import('./ObsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
      // The overlay setup dialog on this page speaks with the Overlays page's words.
      (locale) => import(`../overlays/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;
