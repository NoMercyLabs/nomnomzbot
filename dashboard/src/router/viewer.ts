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

import { merge } from '@/lib/array';
import { viewerChatRoute, viewerExternalChatRoute } from '@/views/dashboard/chat/route';
import { viewerAccountRoute } from '@/views/viewer/account/route';
import { viewerChannelsRoute } from '@/views/viewer/channels/route';
import { viewerMeRoute } from '@/views/viewer/me/route';
import { viewerComingSoonRoutes } from '@/views/viewer/nav/route';

// The pages a signed-in viewer sees, outside the management shell.
export const viewerRoutes: RouteRecordRaw = {
  path: '/me',
  component: () => import('@/layouts/ViewerLayout.vue'),
  meta: { requiresAuth: true },
  children: Array.from(
    merge(
      [
        viewerChannelsRoute,
        viewerMeRoute,
        viewerAccountRoute,
        viewerChatRoute,
        viewerExternalChatRoute,
      ],
      viewerComingSoonRoutes,
    ),
  ),
};
