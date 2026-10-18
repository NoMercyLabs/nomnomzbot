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

const resourceLimitsRoute = {
  path: 'settings/resource-limits',
  name: 'dashboard-settings-resource-limits',
  meta: { navMode: 'settings' },
  component: () =>
    loadPage(
      () => import('./resource-limits/ResourceLimitsView.vue'),
      (locale) => import(`./resource-limits/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

// Settings pages of lane C: Resource limits, Billing, Your data, Channel management.
export const settingsLaneCRoutes: RouteRecordRaw[] = [resourceLimitsRoute];
