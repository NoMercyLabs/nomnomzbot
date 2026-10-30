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

import type { RouteLocationNormalized, RouteLocationRaw, RouteRecordRaw } from 'vue-router';

import { currentUserQuery } from '@/api/auth';
import { ensureQuery } from '@/api/queryClient';
import { holdsPlatformKey } from '@/composables/usePlatformCan';
import { loadPage } from '@/i18n';

import { adminPageKey } from './adminKeys';

// Only a platform admin who holds the page's platform key opens it; anyone else goes to the home page.
// A page with no key in the table needs the admin flag only.
export async function adminOnly(to: RouteLocationNormalized): Promise<true | RouteLocationRaw> {
  const user = await ensureQuery(currentUserQuery);
  const key = adminPageKey(String(to.name));
  const open = key === undefined
    ? user?.isAdmin === true
    : holdsPlatformKey(user, key);

  return open
    ? true
    : { name: 'dashboard-home' };
}

// An old ?tenant=<channelId> link opens that channel's console, with the other filters kept. The admin
// check runs first, so only an admin who holds the page's key is ever sent on.
export function tenantLinkRedirect(to: Pick<RouteLocationNormalized, 'query'>): true | RouteLocationRaw {
  const { tenant, ...query } = to.query;

  return typeof tenant === 'string'
    ? {
        name: 'dashboard-admin-channels-console',
        params: { channelId: tenant },
        query,
      }
    : true;
}

// One list of the people who joined: streamers with their channel, and viewers who signed in.
export const dashboardAdminTenantsRoute = {
  path: 'admin/tenants',
  name: 'dashboard-admin-tenants',
  meta: { navMode: 'admin' },
  beforeEnter: [adminOnly, tenantLinkRedirect],
  component: () =>
    loadPage(
      () => import('./AdminTenantsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminOverviewRoute = {
  path: 'admin/overview',
  name: 'dashboard-admin-overview',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminOverviewView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminEventSubHealthRoute = {
  path: 'admin/eventsub-health',
  name: 'dashboard-admin-eventsub-health',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminEventSubHealthView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminChannelConsoleRoute = {
  path: 'admin/channels/:channelId/:tab?',
  name: 'dashboard-admin-channels-console',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminChannelConsoleView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminWebhookDeliveriesRoute = {
  path: 'admin/webhook-deliveries',
  name: 'dashboard-admin-webhook-deliveries',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminWebhookDeliveriesView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminSpamDefaultsRoute = {
  path: 'admin/spam-defaults',
  name: 'dashboard-admin-spam-defaults',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminSpamDefaultsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminProvidersRoute = {
  path: 'admin/providers',
  name: 'dashboard-admin-providers',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminProvidersView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminFeatureFlagsRoute = {
  path: 'admin/feature-flags',
  name: 'dashboard-admin-feature-flags',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminFeatureFlagsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminScheduledJobsRoute = {
  path: 'admin/scheduled-jobs',
  name: 'dashboard-admin-scheduled-jobs',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminScheduledJobsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminSupportRoute = {
  path: 'admin/support',
  name: 'dashboard-admin-support',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminSupportView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminDataRequestsRoute = {
  path: 'admin/data-requests',
  name: 'dashboard-admin-data-requests',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminDataRequestsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminEventReplayRoute = {
  path: 'admin/event-replay',
  name: 'dashboard-admin-event-replay',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminEventReplayView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminTrustSafetyRoute = {
  path: 'admin/trust-safety',
  name: 'dashboard-admin-trust-safety',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminTrustSafetyView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminTenantUsageRoute = {
  path: 'admin/tenant-usage',
  name: 'dashboard-admin-tenant-usage',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminTenantUsageView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminErrorBudgetRoute = {
  path: 'admin/error-budget',
  name: 'dashboard-admin-error-budget',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminErrorBudgetView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminContentRoute = {
  path: 'admin/content',
  name: 'dashboard-admin-content',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminContentView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminBillingRoute = {
  path: 'admin/billing',
  name: 'dashboard-admin-billing',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminBillingView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminAuditRoute = {
  path: 'admin/audit-log',
  name: 'dashboard-admin-audit-log',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminAuditView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminPlatformBotRoute = {
  path: 'admin/platform-bot',
  name: 'dashboard-admin-platform-bot',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminPlatformBotView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminAccessRoute = {
  path: 'admin/access',
  name: 'dashboard-admin-access',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminAccessView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminSystemRoute = {
  path: 'admin/system',
  name: 'dashboard-admin-system',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminSystemView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;

export const dashboardAdminPlatformDefaultsRoute = {
  path: 'admin/platform-defaults',
  name: 'dashboard-admin-platform-defaults',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminPlatformDefaultsView.vue'),
      (locale) => import(`./i18n/${locale}.json`),
      // The action rows read their plain names from the permissions page's words.
      (locale) => import(`../settings/permissions/i18n/${locale}.json`),
    ),
} as const satisfies RouteRecordRaw;
