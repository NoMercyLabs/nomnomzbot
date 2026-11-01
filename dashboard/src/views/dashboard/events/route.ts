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

// The rows and their replay messages are the Home panel's words, so the page loads those too.
export const dashboardEventsRoute = {
  path: 'events',
  name: 'dashboard-events',
  meta: { action: 'dashboard:read' },
  component: () =>
    loadPage(
      () => import('./EventsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
      (locale) => import(`../home/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;
