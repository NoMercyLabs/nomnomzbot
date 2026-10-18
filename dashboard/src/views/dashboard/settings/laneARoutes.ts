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

const whiteLabelBotRoute = {
  path: 'settings/white-label-bot',
  name: 'dashboard-settings-white-label-bot',
  meta: { navMode: 'settings' },
  component: () =>
    loadPage(
      () => import('./white-label-bot/WhiteLabelBotView.vue'),
      (locale) => import(`./white-label-bot/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

const featuresRoute = {
  path: 'settings/features',
  name: 'dashboard-settings-features',
  meta: { navMode: 'settings' },
  component: () =>
    loadPage(
      () => import('./features/FeaturesView.vue'),
      (locale) => import(`./features/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

// Settings pages of lane A: White-label bot, Features, Integrations, Federation. Each page adds its route here.
export const settingsLaneARoutes: RouteRecordRaw[] = [whiteLabelBotRoute, featuresRoute];
