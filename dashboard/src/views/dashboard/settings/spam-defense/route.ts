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
