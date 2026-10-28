// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';
import { adminHandlers } from '@/realtime/admin';
import { dashboardRoutes } from '@/router/dashboard';

import AdminErrorBudgetView from '../AdminErrorBudgetView.vue';
import en from '../i18n/en.json';
import { dashboardAdminErrorBudgetRoute } from '../route';

type Options = { params?: { query?: Record<string, unknown> } };
type Call = { path: string; query: Record<string, unknown> };

const calls: Call[] = [];
let tenants: Record<string, unknown>[] = [];
let refused: boolean = false;

async function answer(path: string, options?: Options) {
  calls.push({ path, query: { ...options?.params?.query } });

  if (refused)
    return { error: { message: 'no' }, response: { status: 403, ok: false } };

  return {
    data: {
      data: tenants,
      total: tenants.length,
      hasMore: false,
    },
    response: { status: 200, ok: true },
  };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: (path: string, options?: Options) => answer(path, options),
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let queries: QueryClient;

beforeEach(() => {
  calls.length = 0;
  refused = false;

  tenants = [
    {
      broadcasterId: 'b1',
      channelDisplayName: 'Broken_Hooks',
      windowStartUtc: '2026-10-17T10:00:00Z',
      windowEndUtc: '2026-10-18T10:00:00Z',
      attempts: 1200,
      errors: 30,
      errorRate: 0.025,
      targetSuccessRate: 0.99,
      budgetRemainingFraction: -1.5,
    },
    {
      broadcasterId: 'b2',
      channelDisplayName: 'Healthy_One',
      windowStartUtc: '2026-10-17T10:00:00Z',
      windowEndUtc: '2026-10-18T10:00:00Z',
      attempts: 1000,
      errors: 2,
      errorRate: 0.002,
      targetSuccessRate: 0.99,
      budgetRemainingFraction: 0.8,
    },
    {
      broadcasterId: 'b3',
      channelDisplayName: 'No_Attempts',
      windowStartUtc: '2026-10-17T10:00:00Z',
      windowEndUtc: '2026-10-18T10:00:00Z',
      attempts: 0,
      errors: 0,
      errorRate: null,
      targetSuccessRate: 0.99,
      budgetRemainingFraction: null,
    },
  ];

  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  queryClient.clear();
  vi.restoreAllMocks();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(AdminErrorBudgetView, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const text = (): string => document.body.textContent ?? '';
const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-budget-row]'),
];

describe('AdminErrorBudgetView', () => {
  it('renders one row per tenant with attempts, errors and the error rate', async () => {
    await open();

    expect(calls[0]!.path).toBe('/api/v{version}/admin/error-budget');
    expect(rows()).toHaveLength(3);
    expect(rows()[0]!.textContent).toContain('Broken_Hooks');
    expect(rows()[0]!.textContent).toContain('1,200');
    expect(rows()[0]!.textContent).toContain('30');
    expect(rows()[0]!.textContent).toContain('2.5%');
  });

  it('flags a tenant whose budget is spent and shows the remaining share of one that is not', async () => {
    await open();

    expect(rows()[0]!.querySelector('[data-exhausted]')).not.toBeNull();
    expect(rows()[1]!.querySelector('[data-exhausted]')).toBeNull();
    expect(rows()[1]!.textContent).toContain('80%');
  });

  it('says there were no attempts instead of showing a made-up rate', async () => {
    await open();

    expect(rows()[2]!.textContent).toContain('No attempts');
    expect(rows()[2]!.querySelector('[data-exhausted]')).toBeNull();
  });

  it('states the policy target', async () => {
    await open();

    expect(text()).toContain('99%');
  });

  it('shows the forbidden message when the bot refuses', async () => {
    refused = true;
    await open();

    expect(text()).toContain('You may not see the error budgets.');
    expect(rows()).toHaveLength(0);
  });

  it('carries the restricted saas marker', async () => {
    await open();

    expect(text()).toContain('earn money with NomNomzBot');
  });

  it('reloads the budgets when the hub says a delivery attempt resolved', async () => {
    await open();

    const before = calls.length;
    const handler = adminHandlers.find((entry) => entry.event === 'ReceiveErrorBudgetChanged')!;

    handler.handle(
      { queries, channelId: null },
      { broadcasterId: 'b2', status: 'Failed' } as never,
    );

    await settle();

    expect(calls.length).toBeGreaterThan(before);
  });

  it('on a phone stacks each tenant as a card titled with the channel name and the other columns as labeled lines', async () => {
    await open();

    const labels = [...rows()[0]!.querySelectorAll('[data-cell-label]')].map((label) => label.textContent?.trim());

    expect(rows()[0]!.className).toContain('max-md:flex-col');
    expect(rows()[0]!.querySelector('[data-stack=title]')!.textContent).toContain('Broken_Hooks');
    expect(labels).toHaveLength(4);
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-error-budget');

    expect(registered?.path).toBe('admin/error-budget');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminErrorBudgetRoute.beforeEnter);
  });
});
