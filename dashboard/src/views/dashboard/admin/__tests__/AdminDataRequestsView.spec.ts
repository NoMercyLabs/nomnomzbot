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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';
import { adminHandlers } from '@/realtime/admin';
import { dashboardRoutes } from '@/router/dashboard';

import AdminDataRequestsView from '../AdminDataRequestsView.vue';
import en from '../i18n/en.json';
import { dashboardAdminDataRequestsRoute } from '../route';

type Options = {
  params?: { query?: Record<string, unknown>; path?: Record<string, unknown> };
  body?: unknown;
};
type Call = {
  method: string;
  path: string;
  options?: Options;
};

const calls: Call[] = [];

const failed = {
  id: 'e1',
  subjectUserId: 'u1',
  subjectIdHash: 'hash-failed',
  broadcasterId: 'b1',
  requestType: 'erasure',
  requestedBy: 'platform_iam',
  status: 'failed',
  scope: 'deployment',
  rowsAffected: 0,
  failureReason: 'database offline',
  requestedAt: '2026-10-17T10:00:00Z',
};

const done = {
  id: 'e2',
  subjectUserId: 'u2',
  subjectIdHash: 'hash-done',
  requestType: 'export',
  requestedBy: 'self_service',
  status: 'completed',
  scope: 'deployment',
  rowsAffected: 42,
  requestedAt: '2026-10-16T10:00:00Z',
  completedAt: '2026-10-16T10:01:00Z',
};

const summary = {
  total: 5,
  pending: 1,
  running: 0,
  completed: 3,
  failed: 1,
  // cspell:ignore cancelled (the API wire value)
  cancelled: 0,
};

const preview = {
  subjectAlreadyAnonymized: false,
  totalRows: 12,
  categories: [
    { categoryKey: 'chatMessages', rowCount: 10 },
    { categoryKey: 'consents', rowCount: 2 },
  ],
};

// The signed-in admin holds every platform key, so every action button shows.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => {
      calls.push({
        method: 'GET',
        path,
        options,
      });

      if (path.endsWith('/erasure/summary'))
        return { data: { data: summary }, response: { status: 200, ok: true } };

      if (path.endsWith('/erasure/preview'))
        return { data: { data: preview }, response: { status: 200, ok: true } };

      return {
        data: { data: [failed, done] },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        options,
      });

      return { data: { data: {} }, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  localStorage.clear();
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  queryClient.clear();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(AdminDataRequestsView, {
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
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );

async function openSelect(selector: string): Promise<HTMLElement[]> {
  // jsdom lacks these three; the select calls them when it opens.
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;
  Element.prototype.scrollIntoView = (): void => undefined;

  document.body.querySelector<HTMLElement>(selector)!.dispatchEvent(
    new PointerEvent('pointerdown', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  await settle();

  return [...document.body.querySelectorAll<HTMLElement>('[role="option"]')];
}

async function pick(selector: string, label: string): Promise<void> {
  const option = (await openSelect(selector)).find((node) => node.textContent?.trim() === label)!;

  option.dispatchEvent(
    new PointerEvent('pointerup', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  option.click();
  await settle();
}

describe('AdminDataRequestsView', () => {
  it('shows the ledger counts and one row per request with its status, type and failure reason', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-request-row]')).toHaveLength(2);
    expect(text()).toContain('hash-failed');
    expect(text()).toContain('database offline');
    expect(text()).toContain('42');
  });

  it('words each row in plain terms: who asked, and rows exported or erased', async () => {
    await open();

    const rows = [...document.body.querySelectorAll('[data-request-row]')].map(
      (row) => row.textContent ?? '',
    );

    expect(rows[0]).toContain('Requested by a platform admin');
    expect(rows[0]).toContain('0 rows erased');
    expect(rows[1]).toContain('Requested by the person themselves');
    expect(rows[1]).toContain('42 rows exported');
    expect(text()).not.toContain('self_service');
    expect(text()).not.toContain('rows changed');
  });

  it('puts the ledger counts on the status filter and drops the ledger card', async () => {
    await open();

    expect(document.body.querySelector('[data-summary]')).toBeNull();

    const options = (await openSelect('[data-status-filter]')).map((option) =>
      option.textContent?.trim());

    expect(options).toEqual([
      'All statuses (5)',
      'Pending (1)',
      'Running (0)',
      'Completed (3)',
      'Failed (1)',
      'Canceled (0)',
    ]);
  });

  it('shows no subject identity, only the hash', async () => {
    await open();

    expect(text()).not.toContain('u1');
  });

  it('offers a retry on the failed request only', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-retry]')).toHaveLength(1);
  });

  it('sends the chosen status and type to the list and remembers the status across a remount', async () => {
    await open();
    await pick('[data-status-filter]', 'Failed (1)');

    const list = calls.filter((call) => call.path.endsWith('/compliance/erasure')).at(-1)!;

    expect(list.options?.params?.query?.status).toBe('failed');
    expect(localStorage.getItem('nnz.adminDataRequests.status')).toBe('failed');

    mounted?.unmount();
    calls.length = 0;
    await open();

    const again = calls.find((call) => call.path.endsWith('/compliance/erasure'))!;

    expect(again.options?.params?.query?.status).toBe('failed');
  });

  it('counts what a retry would destroy before it posts, then retries that request', async () => {
    await open();
    (document.body.querySelector('[data-retry]') as HTMLElement).click();
    await settle();

    const previewCall = calls.find((call) => call.path.endsWith('/erasure/preview'))!;

    expect(previewCall.options?.params?.query).toMatchObject({
      subjectUserId: 'u1',
      broadcasterId: 'b1',
    });

    expect(text()).toContain('12 rows');
    expect(calls.some((call) => call.method === 'POST')).toBe(false);

    byText('[role="dialog"] button', 'Retry erasure')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/compliance/erasure/{erasureRequestId}/retry');
    expect(post.options?.params?.path?.erasureRequestId).toBe('e1');
  });

  it('reloads the ledger when the hub says a data request changed', () => {
    const invalidate = vi.fn<(filter: { queryKey: string[] }) => void>();
    const handler = adminHandlers.find((entry) => entry.event === 'ReceiveDataRequestsChanged')!;

    handler.handle(
      { queries: { invalidateQueries: invalidate } as unknown as QueryClient, channelId: null },
      {
        change: 'failed',
        erasureRequestId: 'e1',
      } as never,
    );

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['admin', 'dataRequests'] });
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-data-requests');

    expect(registered?.path).toBe('admin/data-requests');
    expect(registered?.beforeEnter).toBe(dashboardAdminDataRequestsRoute.beforeEnter);
  });
});
