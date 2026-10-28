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

import { popoutChatRoute } from '@/views/dashboard/chat/route';

export const popoutRoutes: RouteRecordRaw = {
  path: '/popout',
  component: () => import('@/layouts/PopoutLayout.vue'),
  meta: { requiresAuth: true },
  children: [popoutChatRoute],
};
