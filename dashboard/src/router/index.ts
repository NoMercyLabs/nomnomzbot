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

import { dashboardRoutes } from './dashboard';
import { signInGuard } from './guard';
import { siteRoutes } from './site';

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [siteRoutes, dashboardRoutes],
});

router.beforeEach(signInGuard);

export default router;
