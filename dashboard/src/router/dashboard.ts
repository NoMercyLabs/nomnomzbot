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

import { dashboardAdminChannelsRoute } from '@/views/dashboard/admin/route';
import { dashboardAnalyticsRoute } from '@/views/dashboard/analytics/route';
import { dashboardCatalogRoute } from '@/views/dashboard/catalog/route';
import { dashboardChannelPointsRoute } from '@/views/dashboard/channel-points/route';
import { dashboardChatRoute, dashboardViewerChatRoute } from '@/views/dashboard/chat/route';
import { dashboardCommandsRoute } from '@/views/dashboard/commands/route';
import { dashboardHomeRoute } from '@/views/dashboard/home/route';
import { dashboardDiscordRoute } from '@/views/dashboard/discord/route';
import { dashboardMusicRoute } from '@/views/dashboard/music/route';
import { dashboardObsRoute } from '@/views/dashboard/obs/route';
import { dashboardOverlaysRoute } from '@/views/dashboard/overlays/route';
import { dashboardPeopleRoute } from '@/views/dashboard/people/route';
import { dashboardPipelineRoute, dashboardPipelinesRoute } from '@/views/dashboard/pipelines/route';
import { dashboardPollsRoute } from '@/views/dashboard/polls/route';
import { dashboardScriptsRoute } from '@/views/dashboard/scripts/route';
import { dashboardSettingsRoute } from '@/views/dashboard/settings/route';
import { scaffoldRoutes } from '@/views/dashboard/scaffold/route';
import { dashboardStreamRoute } from '@/views/dashboard/stream/route';
import { dashboardSupportersRoute } from '@/views/dashboard/supporters/route';
import { dashboardTtsRoute } from '@/views/dashboard/tts/route';

// The pages that are built.
const builtRoutes: RouteRecordRaw[] = [
  dashboardHomeRoute,
  dashboardChatRoute,
  dashboardViewerChatRoute,
  dashboardCommandsRoute,
  dashboardMusicRoute,
  dashboardChannelPointsRoute,
  dashboardSupportersRoute,
  dashboardDiscordRoute,
  dashboardTtsRoute,
  dashboardPeopleRoute,
  dashboardOverlaysRoute,
  dashboardObsRoute,
  dashboardPollsRoute,
  dashboardPipelinesRoute,
  dashboardPipelineRoute,
  dashboardScriptsRoute,
  dashboardStreamRoute,
  dashboardAnalyticsRoute,
  dashboardSettingsRoute,
  dashboardAdminChannelsRoute,
];

// Every other planned page of every sidebar opens the empty "coming soon" page.
const builtNames = new Set(builtRoutes.map((route) => String(route.name)));
const comingSoonRoutes = scaffoldRoutes(builtNames);

export const dashboardRoutes: RouteRecordRaw = {
  path: '/app',
  component: () => import('@/layouts/DashboardLayout.vue'),
  meta: { requiresAuth: true },
  children: import.meta.env.DEV
    ? Array.from(merge(builtRoutes, comingSoonRoutes, [dashboardCatalogRoute]))
    : Array.from(merge(builtRoutes, comingSoonRoutes)),
};
