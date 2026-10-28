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

import { loadPage, schemaWords } from '@/i18n';

export const dashboardPipelinesRoute = {
  path: 'pipelines',
  name: 'dashboard-pipelines',
  meta: { action: 'pipelines:read' },
  component: () =>
    loadPage(
      () => import('./PipelinesView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

// The workbench takes the whole width, so it is its own page and not the detail half of the list.
// It names every action and field with the bot's schema labels.
export const dashboardPipelineRoute = {
  path: 'pipelines/:id',
  name: 'dashboard-pipeline',
  meta: { action: 'pipelines:read' },
  component: () =>
    loadPage(
      () => import('./WorkbenchView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
      schemaWords,
    ),
} as const satisfies RouteRecordRaw;
