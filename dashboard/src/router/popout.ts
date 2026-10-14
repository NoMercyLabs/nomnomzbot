// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { RouteRecordRaw } from 'vue-router';

import { popoutChatRoute } from '@/views/dashboard/chat/route';

export const popoutRoutes: RouteRecordRaw = {
  path: '/popout',
  component: () => import('@/layouts/PopoutLayout.vue'),
  meta: { requiresAuth: true },
  children: [popoutChatRoute],
};
