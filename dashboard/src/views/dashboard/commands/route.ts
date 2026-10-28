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

// The built-in editor names reply slots and their variables with the bot's schema labels.
export const dashboardCommandsRoute = {
  path: 'commands',
  name: 'dashboard-commands',
  meta: { action: 'commands:read' },
  component: () =>
    loadPage(
      () => import('./CommandsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
      schemaWords,
    ),
} as const satisfies RouteRecordRaw;
