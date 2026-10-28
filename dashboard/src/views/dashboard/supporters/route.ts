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
