// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { createRouter, createWebHistory } from 'vue-router';

import { channelPrefixGuard } from './channelRoutes';
import { dashboardChannelRoutes, dashboardRoutes } from './dashboard';
import { signInGuard } from './guard';
import { popoutRoutes } from './popout';
import { siteRoutes } from './site';
import { viewerRoutes } from './viewer';

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [siteRoutes, dashboardRoutes, dashboardChannelRoutes, popoutRoutes, viewerRoutes],
});

router.beforeEach(channelPrefixGuard);
router.beforeEach(signInGuard);

export default router;
