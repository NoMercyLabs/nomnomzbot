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

import { homeRoute } from '@/views/site/home/route';
import { privacyRoute, termsRoute } from '@/views/site/legal/route';
import { loginRoute } from '@/views/site/login/route';
import { setupRoute } from '@/views/site/setup/route';

export const siteRoutes: RouteRecordRaw = {
  path: '/',
  component: () => import('@/layouts/SiteLayout.vue'),
  children: [homeRoute, loginRoute, setupRoute, privacyRoute, termsRoute],
};
