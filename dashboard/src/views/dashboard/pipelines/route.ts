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

import { loadPage, schemaWords } from '@/i18n';

export const dashboardPipelinesRoute = {
  path: 'pipelines',
  name: 'dashboard-pipelines',
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
  component: () =>
    loadPage(
      () => import('./WorkbenchView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
      schemaWords,
    ),
} as const satisfies RouteRecordRaw;
