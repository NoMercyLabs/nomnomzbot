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
