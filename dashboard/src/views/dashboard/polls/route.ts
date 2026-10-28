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

// The Start a poll and Prediction dialogs are the home page's; their copy lives with the home words.
export const dashboardPollsRoute = {
  path: 'polls',
  name: 'dashboard-polls',
  meta: { action: 'live-ops:polls:read' },
  component: () =>
    loadPage(
      () => import('./PollsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
      (locale) => import(`../home/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;
