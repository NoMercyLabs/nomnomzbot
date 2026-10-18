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

import AdminWebhookDeliveriesView from '../AdminWebhookDeliveriesView.vue';
import en from '../i18n/en.json';
import { dashboardAdminWebhookDeliveriesRoute } from '../route';

type Options = { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> } };
type Call = {
  method: string;
  path: string;
  query: Record<string, unknown>;
  pathParams: unknown;
};

const calls: Call[] = [];
let deliveries: Record<string, unknown>[] = [];
let total: number = 0;
let refused: boolean = false;

const delivery = (id: number, over: Record<string, unknown> = {}) => ({
  id,
  broadcasterId: 'b1',
  endpointId: 'e1',
  endpointName: 'Discord relay',
  endpointCanReplay: true,
  eventType: 'channel.follow',
  attempt: 1,
  status: 'succeeded',
  responseCode: 204,
  durationMs: 120,
  error: null,
  createdAt: '2026-10-01T10:00:00Z',
  ...over,
}) as const satisfies Record<string, unknown>;

async function answer(method: string, path: string, options?: Options) {
  calls.push({
    method,
    path,
    query: { ...options?.params?.query },
    pathParams: options?.params?.path?.deliveryId,
  });

  if (refused)
    return { error: { message: 'no' }, response: { status: 403, ok: false } };

  if (method === 'POST') {
    return {
      data: { data: {
        originalDeliveryId: 2,
        newDeliveryId: 9,
        status: 'succeeded',
      } },
      response: { status: 200, ok: true },
    };
  }

  return {
    data: {
      data: deliveries,
      total,
      hasMore: total > 25,
      nextPage: 2,
    },
    response: { status: 200, ok: true },
  };
}

// The signed-in admin holds every platform key, so every action button shows.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: (path: string, options?: Options) => answer('GET', path, options),
    POST: (path: string, options?: Options) => answer('POST', path, options),
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  refused = false;
  total = 2;

  deliveries = [
    delivery(1),
    delivery(2, {
      status: 'failed',
      responseCode: 500,
      error: 'upstream down',
      attempt: 2,
    }),
    delivery(3, { endpointCanReplay: false, endpointName: 'Deleted hook' }),
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
  mounted = mount(AdminWebhookDeliveriesView, {
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
  ...document.body.querySelectorAll<HTMLElement>('[data-delivery-row]'),
];
const replayButton = (row: HTMLElement): HTMLButtonElement | null =>
  [...row.querySelectorAll<HTMLButtonElement>('button')].find(
    (node) => node.textContent?.trim() === 'Replay',
  ) ?? null;

describe('AdminWebhookDeliveriesView', () => {
  it('renders one row per attempt with endpoint, event, status, code, duration and error', async () => {
    await open();

    expect(calls[0]!.path).toBe('/api/v{version}/admin/webhooks/deliveries');
    expect(rows()).toHaveLength(3);
    expect(rows()[0]!.textContent).toContain('Discord relay');
    expect(rows()[0]!.textContent).toContain('channel.follow');
    expect(rows()[0]!.textContent).toContain('succeeded');
    expect(rows()[0]!.textContent).toContain('204');
    expect(rows()[0]!.textContent).toContain('120 ms');
    expect(rows()[1]!.textContent).toContain('upstream down');
    expect(rows()[1]!.querySelector('[data-tone="bad"]')).not.toBeNull();
  });

  it('replays one delivery by id, then reads the log again', async () => {
    await open();
    calls.length = 0;

    replayButton(rows()[1]!)!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/admin/webhooks/deliveries/{deliveryId}/replay');
    expect(post.pathParams).toBe(2);
    expect(calls.some((call) => call.method === 'GET')).toBe(true);
  });

  it('offers no replay when the endpoint is gone', async () => {
    await open();

    expect(replayButton(rows()[2]!)).toBeNull();
  });

  it('shows the forbidden message when the bot refuses', async () => {
    refused = true;
    await open();

    expect(text()).toContain('You may not see the webhook deliveries.');
    expect(rows()).toHaveLength(0);
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-webhook-deliveries');

    expect(registered?.path).toBe('admin/webhook-deliveries');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminWebhookDeliveriesRoute.beforeEnter);
  });
});
