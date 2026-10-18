// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { RouteLocationRaw, RouteRecordRaw } from 'vue-router';

import { currentUserQuery } from '@/api/auth';
import { queryClient } from '@/api/queryClient';
import { loadPage } from '@/i18n';

// Only a platform admin opens the page; anyone else goes to the home page.
export async function adminOnly(): Promise<true | RouteLocationRaw> {
  const user = await queryClient.ensureQueryData(currentUserQuery);

  return user?.isAdmin === true
    ? true
    : { name: 'dashboard-home' };
}

export const dashboardAdminChannelsRoute = {
  path: 'admin/channels',
  name: 'dashboard-admin-channels',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminChannelsView.vue'),
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

export const dashboardAdminUsersRoute = {
  path: 'admin/users',
  name: 'dashboard-admin-users',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminUsersView.vue'),
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

export const dashboardAdminTenantsRoute = {
  path: 'admin/tenants',
  name: 'dashboard-admin-tenants',
  meta: { navMode: 'admin' },
  beforeEnter: adminOnly,
  component: () =>
    loadPage(
      () => import('./AdminTenantsView.vue'),
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
