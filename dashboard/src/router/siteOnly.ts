// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { createRouter, createWebHistory, type RouteRecordRaw, type Router } from 'vue-router';

import { homeRoute } from '@/views/site/home/route';
import { loginRoute } from '@/views/site/login/route';
import { notFoundRoute } from '@/views/site/not-found/route';

// The public site build (static hosting, no bot behind it): the home page, the "Coming soon" login and a
// not-found page. The app and the setup wizard need a bot, so their paths lead back to the home page.
export const siteOnlyRoutes: RouteRecordRaw[] = [
  {
    path: '/',
    component: () => import('@/layouts/SiteLayout.vue'),
    children: [homeRoute, loginRoute],
  },
  { path: '/app/:rest(.*)*', redirect: '/' },
  { path: '/setup', redirect: '/' },
  notFoundRoute,
];

export function createSiteRouter(): Router {
  return createRouter({
    history: createWebHistory(import.meta.env.BASE_URL),
    routes: siteOnlyRoutes,
  });
}
