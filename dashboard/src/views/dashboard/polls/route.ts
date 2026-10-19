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
