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

import { homeRoute } from '@/views/site/home/route'
import { loginRoute } from '@/views/site/login/route'

export const siteRoutes: RouteRecordRaw = {
  path: '/',
  component: () => import('@/layouts/SiteLayout.vue'),
  children: [homeRoute, loginRoute],
}
