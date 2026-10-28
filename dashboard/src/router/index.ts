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

import { createRouter, createWebHistory } from 'vue-router';

import { accessGuard } from './accessGuard';
import { channelScopeGuard } from './channelScope';
import { channelPrefixGuard, ownChannelGuard, platformPathGuard } from './channelRoutes';
import { dashboardChannelRoutes, dashboardRoutes } from './dashboard';
import { setupGuard, signInGuard } from './guard';
import { popoutRoutes } from './popout';
import { rungGuard } from './rungGuard';
import { siteRoutes } from './site';
import { viewerRoutes } from './viewer';

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [siteRoutes, dashboardRoutes, dashboardChannelRoutes, popoutRoutes, viewerRoutes],
});

router.beforeEach(platformPathGuard);
router.beforeEach(channelPrefixGuard);
router.beforeEach(setupGuard);
router.beforeEach(signInGuard);
router.beforeEach(ownChannelGuard);
router.beforeEach(rungGuard());
router.beforeEach(channelScopeGuard);
router.beforeEach(accessGuard(router));

export default router;
