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

// Development builds only (the router adds it behind import.meta.env.DEV).
export const dashboardCatalogRoute = {
  path: 'catalog',
  name: 'dashboard-catalog',
  // A component showcase with no channel data and no server call, so there is no action key to name.
  meta: { open: true },
  component: () =>
    loadPage(
      () => import('./CatalogView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
      // The page recipes tab draws a real AdminCollectionPage, which prints the admin pager and crumb words.
      (locale) => import(`../admin/i18n/${locale}.json`),
      // The Setup shell example draws the real StepsFrame, which prints the setup step words.
      (locale) => import(`../../site/setup/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;
