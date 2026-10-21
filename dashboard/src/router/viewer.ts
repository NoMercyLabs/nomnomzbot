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
