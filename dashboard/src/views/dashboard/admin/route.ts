// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { RouteLocationRaw, RouteRecordRaw } from 'vue-router';

import { currentUserQuery } from '@/api/auth';
import { queryClient } from '@/api/queryClient';
import { loadPage } from '@/i18n';

// Only a platform admin opens the page; anyone else goes to the home page.
export async function adminOnly(): Promise<true | RouteLocationRaw> {
  const user = await queryClient.ensureQueryData(currentUserQuery);

  return user?.isAdmin === true
    ? true
    : { name: 'dashboard-home' };
}

export const dashboardAdminChannelsRoute = {
  path: 'admin/channels',
  name: 'dashboard-admin-channels',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminChannelsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminOverviewRoute = {
  path: 'admin/overview',
  name: 'dashboard-admin-overview',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminOverviewView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminUsersRoute = {
  path: 'admin/users',
  name: 'dashboard-admin-users',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminUsersView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminSpamDefaultsRoute = {
  path: 'admin/spam-defaults',
  name: 'dashboard-admin-spam-defaults',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminSpamDefaultsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;
