// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { RouteLocationRaw, RouteRecordRaw } from 'vue-router'

import { currentUserQuery } from '@/api/auth'
import { queryClient } from '@/api/queryClient'
import { loadPage } from '@/i18n'

// Only a platform admin opens the page; anyone else goes to the home page.
async function adminOnly(): Promise<true | RouteLocationRaw> {
  const user = await queryClient.ensureQueryData(currentUserQuery)

  return user?.isAdmin === true ? true : { name: 'dashboard-home' }
}

export const dashboardAdminChannelsRoute = {
  path: 'admin/channels',
  name: 'dashboard-admin-channels',
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminChannelsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw
