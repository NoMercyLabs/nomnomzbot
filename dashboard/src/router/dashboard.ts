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

import { dashboardAdminChannelsRoute } from '@/views/dashboard/admin/route'
import { dashboardCatalogueRoute } from '@/views/dashboard/catalogue/route'
import { dashboardChatRoute } from '@/views/dashboard/chat/route'
import { dashboardCommandsRoute } from '@/views/dashboard/commands/route'
import { dashboardHomeRoute } from '@/views/dashboard/home/route'
import { dashboardMusicRoute } from '@/views/dashboard/music/route'
import { dashboardOverlaysRoute } from '@/views/dashboard/overlays/route'
import { dashboardPeopleRoute } from '@/views/dashboard/people/route'
import { dashboardScriptsRoute } from '@/views/dashboard/scripts/route'
import { dashboardSettingsRoute } from '@/views/dashboard/settings/route'
import { dashboardStreamRoute } from '@/views/dashboard/stream/route'
import { dashboardTtsRoute } from '@/views/dashboard/tts/route'

export const dashboardRoutes: RouteRecordRaw = {
  path: '/app',
  component: () => import('@/layouts/DashboardLayout.vue'),
  meta: { requiresAuth: true },
  children: import.meta.env.DEV
    ? [
        dashboardHomeRoute,
        dashboardChatRoute,
        dashboardCommandsRoute,
        dashboardMusicRoute,
        dashboardTtsRoute,
        dashboardPeopleRoute,
        dashboardOverlaysRoute,
        dashboardScriptsRoute,
        dashboardStreamRoute,
        dashboardSettingsRoute,
        dashboardAdminChannelsRoute,
        dashboardCatalogueRoute,
      ]
    : [
        dashboardHomeRoute,
        dashboardChatRoute,
        dashboardCommandsRoute,
        dashboardMusicRoute,
        dashboardTtsRoute,
        dashboardPeopleRoute,
        dashboardOverlaysRoute,
        dashboardScriptsRoute,
        dashboardStreamRoute,
        dashboardSettingsRoute,
        dashboardAdminChannelsRoute,
      ],
}
