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

import { loadPage } from '@/i18n';
import { supporterSourcesComponent } from '@/views/dashboard/supporters/route';

import { settingsApiDevicesRoute } from './api-devices/route';
import { settingsCustomEventFeedsRoute } from './custom-event-feeds/route';
import { settingsBillingRoute } from './billing/route';
import { settingsChannelManagementRoute } from './channel-management/route';
import { settingsEventJournalRoute } from './event-journal/route';
import { settingsFeaturesRoute } from './features/route';
import { settingsFederationRoute } from './federation/route';
import { settingsIntegrationsRoute } from './integrations/route';
import { settingsPermissionsRoute } from './permissions/route';
import { settingsResourceLimitsRoute } from './resource-limits/route';
import { settingsEscalationRoute } from './escalation/route';
import { settingsSpamDefenseRoute } from './spam-defense/route';
import { settingsTrustPolicyRoute } from './trust-policy/route';
import { settingsWebhooksRoute } from './webhooks/route';
import { settingsYourDataRoute } from './your-data/route';
import { settingsWhiteLabelBotRoute } from './white-label-bot/route';

const settingsPage = () =>
  loadPage(
    () => import('./SettingsView.vue'),
    (locale) => import(`./i18n/${locale}.json`),
  );

// One route per sidebar entry of the settings. Each page opens on its own; there is no inner menu.
export const dashboardSettingsRoute = {
  path: 'settings',
  name: 'dashboard-settings',
  meta: {
    navMode: 'settings',
    userLevel: true,
    // Readability is a person-level preference, not a channel setting.
    open: true,
  },
  props: { view: 'readability' },
  component: settingsPage,
} as const satisfies RouteRecordRaw;

const botBasicsRoute = {
  path: 'settings/bot-basics',
  name: 'dashboard-settings-bot-basics',
  meta: {
    navMode: 'settings',
    action: 'dashboard:read',
  },
  props: { view: 'basics' },
  component: settingsPage,
} as const satisfies RouteRecordRaw;

const botPersonalityRoute = {
  path: 'settings/bot-personality',
  name: 'dashboard-settings-bot-personality',
  meta: {
    navMode: 'settings',
    action: 'dashboard:read',
  },
  props: { view: 'personality' },
  component: settingsPage,
} as const satisfies RouteRecordRaw;

const engagementTriggersRoute = {
  path: 'settings/engagement-triggers',
  name: 'dashboard-settings-engagement-triggers',
  meta: {
    navMode: 'settings',
    action: 'engagement:read',
  },
  props: { view: 'engagement' },
  component: settingsPage,
} as const satisfies RouteRecordRaw;

// Supporter sources already have their real page; the settings entry opens that same page.
const supporterSourcesRoute = {
  path: 'settings/supporter-sources',
  name: 'dashboard-settings-supporter-sources',
  meta: {
    navMode: 'settings',
    action: 'supporters:read',
  },
  component: supporterSourcesComponent,
} as const satisfies RouteRecordRaw;

export const dashboardSettingsRoutes: RouteRecordRaw[] = [
  dashboardSettingsRoute,
  botBasicsRoute,
  botPersonalityRoute,
  engagementTriggersRoute,
  supporterSourcesRoute,
  settingsWhiteLabelBotRoute,
  settingsFeaturesRoute,
  settingsIntegrationsRoute,
  settingsApiDevicesRoute,
  settingsResourceLimitsRoute,
  settingsBillingRoute,
  settingsCustomEventFeedsRoute,
  settingsWebhooksRoute,
  settingsTrustPolicyRoute,
  settingsEscalationRoute,
  settingsSpamDefenseRoute,
  settingsEventJournalRoute,
  settingsYourDataRoute,
  settingsFederationRoute,
  settingsPermissionsRoute,
  settingsChannelManagementRoute,
];
