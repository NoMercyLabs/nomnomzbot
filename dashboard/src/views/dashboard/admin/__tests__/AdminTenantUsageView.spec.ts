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
import { dashboardRoutes } from '@/router/dashboard';

import AdminTenantUsageView from '../AdminTenantUsageView.vue';
import en from '../i18n/en.json';
import { dashboardAdminTenantUsageRoute } from '../route';

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

beforeEach(() => {
  calls.length = 0;
  refused = false;

  tenants = [
    {
      broadcasterId: 'b1',
      channelDisplayName: 'Stoney_Eagle',
      periodStart: '2026-10-01T00:00:00Z',
      periodEnd: '2026-11-01T00:00:00Z',
      metrics: [
        {
          metricKey: 'chat.messages',
          quantity: 1200,
          costMinorUnits: 250,
          currency: 'EUR',
        },
        {
          metricKey: 'ai.tokens',
          quantity: 40000,
          costMinorUnits: null,
          currency: null,
        },
      ],
      ttsCharacterCount: 9000,
      ttsCostMinorUnits: 150,
      ttsCurrency: 'EUR',
      totalCostsByCurrency: [{ currency: 'EUR', minorUnits: 400 }],
      unpricedUnitKeys: ['ai.tokens'],
    },
    {
      broadcasterId: 'b2',
      channelDisplayName: 'Quiet_One',
      periodStart: '2026-10-01T00:00:00Z',
      periodEnd: '2026-11-01T00:00:00Z',
      metrics: [],
      ttsCharacterCount: 0,
      ttsCostMinorUnits: null,
      ttsCurrency: null,
      totalCostsByCurrency: null,
      unpricedUnitKeys: null,
    },
  ];

  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
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
  mounted = mount(AdminTenantUsageView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
}

const text = (): string => document.body.textContent ?? '';
const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-usage-row]'),
];

describe('AdminTenantUsageView', () => {
  it('renders one row per tenant with its metrics and the priced total', async () => {
    await open();

    expect(calls[0]!.path).toBe('/api/v{version}/admin/usage');
    expect(rows()).toHaveLength(2);
    expect(rows()[0]!.textContent).toContain('Stoney_Eagle');
    expect(rows()[0]!.textContent).toContain('chat.messages');
    expect(rows()[0]!.textContent).toContain('1,200');
    expect(rows()[0]!.textContent).toContain('9,000');
    expect(rows()[0]!.textContent).toContain('4.00');
  });

  it('names an unpriced unit instead of reading it as free', async () => {
    await open();

    const unpriced = rows()[0]!.querySelector('[data-unpriced]');

    expect(unpriced?.textContent).toContain('ai.tokens');
    expect(rows()[1]!.querySelector('[data-unpriced]')).toBeNull();
  });

  it('shows the forbidden message when the bot refuses', async () => {
    refused = true;
    await open();

    expect(text()).toContain('You may not see tenant usage.');
    expect(rows()).toHaveLength(0);
  });

  it('carries the restricted saas marker', async () => {
    await open();

    expect(text()).toContain('reserved to NoMercy Labs');
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-tenant-usage');

    expect(registered?.path).toBe('admin/tenant-usage');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminTenantUsageRoute.beforeEnter);
  });
});
