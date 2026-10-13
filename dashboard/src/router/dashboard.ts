// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { RouteRecordRaw } from 'vue-router'

import { dashboardCatalogueRoute } from '@/views/dashboard/catalogue/route'
import { dashboardHomeRoute } from '@/views/dashboard/home/route'

export const dashboardRoutes: RouteRecordRaw = {
  path: '/app',
  component: () => import('@/layouts/DashboardLayout.vue'),
  meta: { requiresAuth: true },
  children: import.meta.env.DEV ? [dashboardHomeRoute, dashboardCatalogueRoute] : [dashboardHomeRoute],
}
