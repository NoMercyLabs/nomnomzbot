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

// The form's copy lives with the platform defaults page, so this route loads those words beside its own.
export const settingsSpamDefenseRoute = {
  path: 'settings/spam-defense',
  name: 'dashboard-settings-spam-defense',
  meta: {
    navMode: 'settings',
    action: 'spam:policy:read',
  },
  component: () =>
    loadPage(
      () => import('./SpamDefenseView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
      (locale) => import(`../../admin/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;
