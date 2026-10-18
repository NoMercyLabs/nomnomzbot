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

const apiDevicesRoute = {
  path: 'settings/api-devices',
  name: 'dashboard-settings-api-devices',
  meta: { navMode: 'settings' },
  component: () =>
    loadPage(
      () => import('./api-devices/ApiDevicesView.vue'),
      (locale) => import(`./api-devices/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

// Settings pages of lane B: API and devices, Webhooks, Custom event feeds, Permissions, Event journal.
export const settingsLaneBRoutes: RouteRecordRaw[] = [apiDevicesRoute];
