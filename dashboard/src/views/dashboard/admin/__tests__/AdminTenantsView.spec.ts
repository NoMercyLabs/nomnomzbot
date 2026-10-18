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
import { createMemoryHistory, createRouter } from 'vue-router';

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';
import { dashboardRoutes } from '@/router/dashboard';

import AdminTenantsView from '../AdminTenantsView.vue';
import en from '../i18n/en.json';
import { dashboardAdminTenantsRoute } from '../route';

type Options = {
  params?: { path?: Record<string, string>; query?: Record<string, unknown> };
  body?: Record<string, unknown>;
};
type Call = {
  method: string;
  path: string;
  options: Options;
};

const calls: Call[] = [];

const tenantRow = {
  id: 't1',
  name: 'stoney_eagle',
  twitchChannelId: '1',
  status: 'active',
  billingTierKey: 'free',
  isLive: false,
  createdAt: '2026-06-29T10:00:00Z',
  suspendedAt: null,
};

const detail = {
  ...tenantRow,
  suspendedReason: null,
  deploymentMode: 'saas',
  ownerUserId: 'u1',
  ownerDisplayName: 'Stoney_Eagle',
  membershipCount: 4,
};

// The signed-in admin holds every platform key, so every action button shows.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => {
  const answer = (method: string, path: string): Record<string, unknown> => {
    if (path.endsWith('/admin/tenants'))
      return { data: { data: [tenantRow], total: 1 } };

    if (path.endsWith('/members'))
      return {
        data: {
          data: [
            {
              userId: 'u2',
              username: 'quiet_one',
              displayName: 'Quiet_One',
              relation: 'moderator',
              managementRole: 'moderator',
              communityStanding: null,
            },
          ],
          total: 1,
        },
      };

    if (path.endsWith('/limits') && method === 'GET')
      return {
        data: {
          data: [
            {
              id: 'l1',
              broadcasterId: 't1',
              limitKey: 'commands.max',
              limitValue: 500,
              reason: 'Partner deal',
              grantedByPrincipalId: 'p1',
              createdAt: '2026-10-01T10:00:00Z',
              expiresAt: null,
            },
          ],
        },
      };

    if (path.endsWith('/erase/preview'))
      return {
        data: {
          data: {
            channelName: 'stoney_eagle',
            blastRadius: { categories: [], isMinimum: false },
            externalConsequences: [],
            restoreWindowDays: 30,
            permanentAfterUtc: '2026-11-18T10:00:00Z',
          },
        },
      };

    if (path.endsWith('/export'))
      return { data: { data: '{"channel":"stoney_eagle"}' } };

    if (path.endsWith('/remigrate'))
      return {
        data: { data: { migrationsAppliedThisCall: ['M1'], stillPending: [] } },
      };

    if (path.endsWith('/admin/tenants/{broadcasterId}'))
      return { data: { data: detail } };

    return { data: { data: {} } };
  };

  const handle = async (method: string, path: string, options?: Options) => {
    calls.push({
      method,
      path,
      options: options ?? {},
    });

    return { ...answer(method, path), response: { status: 200, ok: true } };
  };

  return {
    client: {
      use: () => undefined,
      GET: (path: string, options?: Options) => handle('GET', path, options),
      POST: (path: string, options?: Options) => handle('POST', path, options),
      PUT: (path: string, options?: Options) => handle('PUT', path, options),
      DELETE: (path: string, options?: Options) => handle('DELETE', path, options),
    },
  };
});

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  localStorage.clear();
  calls.length = 0;
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

async function open(tenant = 't1'): Promise<void> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/admin/tenants',
        name: 'dashboard-admin-tenants',
        component: { template: '<div />' },
      },
    ],
  });

  await router.push(tenant
    ? `/app/admin/tenants?tenant=${tenant}`
    : '/app/admin/tenants');

  mounted = mount(AdminTenantsView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
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
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );
const sent = (method: string, suffix: string): Call | undefined =>
  calls.find((call) => call.method === method && call.path.endsWith(suffix));

async function type(selector: string, value: string): Promise<void> {
  const field = document.body.querySelector<HTMLInputElement>(selector)!;

  field.value = value;
  field.dispatchEvent(new Event('input'));
  await settle();
}

describe('AdminTenantsView', () => {
  it('shows the detail, the members and the limits of the picked tenant, with the saas marker', async () => {
    await open();

    expect(text()).toContain('Stoney_Eagle');
    expect(text()).toContain('saas');
    expect(text()).toContain('Quiet_One');
    expect(text()).toContain('commands.max');
    expect(text()).toContain('Partner deal');
    expect(text()).toContain('restricted option');
  });

  it('suspends with the chosen status and the typed reason', async () => {
    await open();
    byText('button', 'Suspend')!.click();
    await settle();

    await type('[data-suspend-reason]', 'Abuse reports');
    document.body.querySelector<HTMLElement>('[data-form-action]')!.click();
    await settle();

    const call = sent('POST', '/suspend')!;

    expect(call.options.params?.path?.broadcasterId).toBe('t1');
    expect(call.options.body).toEqual({ newStatus: 'suspended', reason: 'Abuse reports' });
  });

  it('saves a limit override with a numeric value and clears one by key', async () => {
    await open();
    byText('button', 'Add limit')!.click();
    await settle();

    await type('[data-limit-key]', 'pipelines.max');
    await type('[data-limit-value]', '12');
    await type('[data-limit-reason]', 'Trial');
    document.body.querySelector<HTMLElement>('[data-form-action]')!.click();
    await settle();

    expect(sent('PUT', '/limits')!.options.body).toEqual({
      limitKey: 'pipelines.max',
      limitValue: 12,
      reason: 'Trial',
      expiresAt: null,
    });

    document.body.querySelector<HTMLElement>('[data-clear-limit="commands.max"]')!.click();
    await settle();

    expect(sent('DELETE', '/limits/{limitKey}')!.options.params?.path?.limitKey).toBe(
      'commands.max',
    );
  });

  it('forces a re-migration with the typed justification and shows what it applied', async () => {
    await open();
    byText('button', 'Re-run migrations')!.click();
    await settle();

    await type('[data-justification]', 'Stuck after deploy');
    document.body.querySelector<HTMLElement>('[data-form-action]')!.click();
    await settle();

    expect(sent('POST', '/remigrate')!.options.body).toEqual({
      justification: 'Stuck after deploy',
    });

    expect(text()).toContain('M1');
  });

  it('previews an erase and only erases with a justification', async () => {
    await open();
    byText('button', 'Erase channel')!.click();
    await settle();

    expect(sent('GET', '/erase/preview')).toBeDefined();
    expect(text()).toContain('30');

    document.body.querySelector<HTMLElement>('[data-form-action]')!.click();
    await settle();
    expect(sent('POST', '/erase')).toBeUndefined();

    await type('[data-justification]', 'Owner request');
    document.body.querySelector<HTMLElement>('[data-form-action]')!.click();
    await settle();

    expect(sent('POST', '/erase')!.options.body).toEqual({ justification: 'Owner request' });
  });

  it('asks for the export of the picked tenant', async () => {
    URL.createObjectURL = vi.fn<() => string>(() => 'blob:x');
    URL.revokeObjectURL = vi.fn<() => void>();
    await open();
    byText('button', 'Export data')!.click();
    await settle();

    expect(sent('GET', '/export')!.options.params?.path?.broadcasterId).toBe('t1');
  });

  it('registers an admin-only route for the tenants page', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-tenants');

    expect(registered?.path).toBe('admin/tenants');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminTenantsRoute.beforeEnter);
  });
});
