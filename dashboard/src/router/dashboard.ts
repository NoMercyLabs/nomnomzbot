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

import { channelVariants } from './channelRoutes';

import {
  dashboardAdminAccessRoute,
  dashboardAdminDataRequestsRoute,
  dashboardAdminAuditRoute,
  dashboardAdminChannelsRoute,
  dashboardAdminEventSubHealthRoute,
  dashboardAdminFeatureFlagsRoute,
  dashboardAdminOverviewRoute,
  dashboardAdminProvidersRoute,
  dashboardAdminScheduledJobsRoute,
  dashboardAdminSpamDefaultsRoute,
  dashboardAdminSupportRoute,
  dashboardAdminBillingRoute,
  dashboardAdminContentRoute,
  dashboardAdminPlatformBotRoute,
  dashboardAdminTenantsRoute,
  dashboardAdminErrorBudgetRoute,
  dashboardAdminTenantUsageRoute,
  dashboardAdminTrustSafetyRoute,
  dashboardAdminUsersRoute,
  dashboardAdminWebhookDeliveriesRoute,
} from '@/views/dashboard/admin/route';
import { dashboardAnalyticsRoute } from '@/views/dashboard/analytics/route';
import { dashboardCatalogRoute } from '@/views/dashboard/catalog/route';
import { dashboardChannelPointsRoute } from '@/views/dashboard/channel-points/route';
import { dashboardChatRoute, dashboardViewerChatRoute } from '@/views/dashboard/chat/route';
import { dashboardCommandsRoute } from '@/views/dashboard/commands/route';
import { dashboardGamesRoute } from '@/views/dashboard/games/route';
import { dashboardHomeRoute } from '@/views/dashboard/home/route';
import { dashboardDiscordRoute } from '@/views/dashboard/discord/route';
import { dashboardMusicRoute } from '@/views/dashboard/music/route';
import { dashboardObsRoute } from '@/views/dashboard/obs/route';
import { dashboardOverlaysRoute } from '@/views/dashboard/overlays/route';
import { dashboardPeopleRoute } from '@/views/dashboard/people/route';
import { dashboardPipelineRoute, dashboardPipelinesRoute } from '@/views/dashboard/pipelines/route';
import { dashboardMediaShareRoute } from '@/views/dashboard/media-share/route';
import { dashboardGiveawaysRoute } from '@/views/dashboard/giveaways/route';
import { dashboardPollsRoute } from '@/views/dashboard/polls/route';
import { dashboardScriptsRoute } from '@/views/dashboard/scripts/route';
import { dashboardSettingsRoutes } from '@/views/dashboard/settings/route';
import { dashboardEconomyRoute } from '@/views/dashboard/economy/route';
import { scaffoldRoutes } from '@/views/dashboard/scaffold/route';
import { dashboardStreamRoute } from '@/views/dashboard/stream/route';
import { dashboardSupportersRoute } from '@/views/dashboard/supporters/route';
import { dashboardEventResponsesRoute } from '@/views/dashboard/event-responses/route';
import { dashboardTimersRoute } from '@/views/dashboard/timers/route';
import { dashboardTtsRoute } from '@/views/dashboard/tts/route';
import { dashboardVoiceTriggersRoute } from '@/views/dashboard/voice-triggers/route';
import { dashboardVtsRoute } from '@/views/dashboard/vts/route';

// The pages that are built.
const builtRoutes: RouteRecordRaw[] = [
  dashboardHomeRoute,
  dashboardChatRoute,
  dashboardViewerChatRoute,
  dashboardCommandsRoute,
  dashboardTimersRoute,
  dashboardMediaShareRoute,
  dashboardEventResponsesRoute,
  dashboardEconomyRoute,
  dashboardMusicRoute,
  dashboardChannelPointsRoute,
  dashboardSupportersRoute,
  dashboardDiscordRoute,
  dashboardTtsRoute,
  dashboardPeopleRoute,
  dashboardOverlaysRoute,
  dashboardObsRoute,
  dashboardVtsRoute,
  dashboardPollsRoute,
  dashboardVoiceTriggersRoute,
  dashboardGiveawaysRoute,
  dashboardGamesRoute,
  dashboardPipelinesRoute,
  dashboardPipelineRoute,
  dashboardScriptsRoute,
  dashboardStreamRoute,
  dashboardAnalyticsRoute,
  ...dashboardSettingsRoutes,
  dashboardAdminChannelsRoute,
  dashboardAdminEventSubHealthRoute,
  dashboardAdminWebhookDeliveriesRoute,
  dashboardAdminOverviewRoute,
  dashboardAdminUsersRoute,
  dashboardAdminSpamDefaultsRoute,
  dashboardAdminFeatureFlagsRoute,
  dashboardAdminTenantsRoute,
  dashboardAdminProvidersRoute,
  dashboardAdminScheduledJobsRoute,
  dashboardAdminSupportRoute,
  dashboardAdminBillingRoute,
  dashboardAdminAccessRoute,
  dashboardAdminDataRequestsRoute,
  dashboardAdminTrustSafetyRoute,
  dashboardAdminTenantUsageRoute,
  dashboardAdminErrorBudgetRoute,
  dashboardAdminAuditRoute,
  dashboardAdminPlatformBotRoute,
];

// Every other planned page of every sidebar opens the empty "coming soon" page.
const builtNames = new Set(builtRoutes.map((route) => String(route.name)));
const comingSoonRoutes = scaffoldRoutes(builtNames);

const dashboardPages: RouteRecordRaw[] = import.meta.env.DEV
  ? Array.from(merge(builtRoutes, comingSoonRoutes, [dashboardCatalogRoute]))
  : Array.from(merge(builtRoutes, comingSoonRoutes));

const dashboardLayout = () => import('@/layouts/DashboardLayout.vue');

export const dashboardRoutes: RouteRecordRaw = {
  path: '/app',
  component: dashboardLayout,
  meta: { requiresAuth: true },
  children: dashboardPages,
};

// The same pages for one named channel: /app/u/<channelName>/<page>.
export const dashboardChannelRoutes: RouteRecordRaw = {
  path: '/app/u/:channelName',
  component: dashboardLayout,
  meta: { requiresAuth: true },
  children: channelVariants(dashboardPages),
};
