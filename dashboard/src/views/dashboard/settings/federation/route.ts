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

export const settingsFederationRoute = {
  path: 'settings/federation',
  name: 'dashboard-settings-federation',
  meta: {
    navMode: 'settings',
    action: 'federation:optin:read',
  },
  component: () =>
    loadPage(
      () => import('./FederationView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;
