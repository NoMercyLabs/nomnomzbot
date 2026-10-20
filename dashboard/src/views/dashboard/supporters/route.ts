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

// The sources page lives under Settings (its route is declared there); it shares this page's translations.
export const supporterSourcesComponent = () =>
  loadPage(
    () => import('./SupporterSourcesView.vue'),
    (locale) => import(`./i18n/${locale}.json`),
  );

export const dashboardSupportersRoute = {
  path: 'supporters',
  name: 'dashboard-supporters',
  meta: { action: 'supporters:read' },
  component: () =>
    loadPage(
      () => import('./SupportersView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;
