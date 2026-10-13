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

import { loginRoute } from '@/views/site/login/route'

// Until the site home exists, the site root opens the sign-in page.
const siteRootRoute = { path: '', redirect: { name: loginRoute.name } } as const satisfies RouteRecordRaw

export const siteRoutes: RouteRecordRaw = {
  path: '/',
  component: () => import('@/layouts/SiteLayout.vue'),
  children: [siteRootRoute, loginRoute],
}
