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

import AdminEventReplayView from '../AdminEventReplayView.vue';
import en from '../i18n/en.json';
import { dashboardAdminEventReplayRoute } from '../route';

type Options = {
  params?: { query?: Record<string, unknown>; path?: Record<string, unknown> };
  body?: Record<string, unknown>;
};
type Call = {
  method: string;
  path: string;
  options?: Options;
};

const calls: Call[] = [];

const projection = {
  projectionName: 'chat-history',
  isGlobal: false,
  subscribedEventTypes: ['chat.message', 'chat.cleared'],
};

const tenant = {
  id: 'b1',
  name: 'Alpha',
  twitchChannelId: '1',
  status: 'active',
  billingTierKey: 'free',
  isLive: false,
  createdAt: '2026-01-01T00:00:00Z',
};

const past = {
  id: 7,
  principalId: 'p1',
  principalType: 'Employee',
  permission: 'eventstore:admin-replay',
  targetBroadcasterId: 'b1',
  targetResource: 'chat-history',
  justification: 'actor=p1;projection=chat-history;count=42',
  breakGlass: false,
  outcome: 'Allowed',
  occurredAt: '2026-10-17T10:00:00Z',
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

      if (path.endsWith('/eventstore/projections'))
        return { data: { data: [projection] }, response: { status: 200, ok: true } };

      if (path.endsWith('/admin/tenants'))
        return { data: { data: [tenant] }, response: { status: 200, ok: true } };

      return { data: { data: [past] }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        options,
      });

      if (path.endsWith('/replay/preview'))
        return {
          data: { data: { ...options?.body, matchingEventCount: 42 } },
          response: { status: 200, ok: true },
        };

      return {
        data: { data: { ...options?.body, appliedCount: 42 } },
        response: { status: 200, ok: true },
      };
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
  mounted = mount(AdminEventReplayView, {
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

type Field = HTMLElement & { value: string; disabled: boolean };

const field = (selector: string): Field => document.body.querySelector<Field>(selector)!;
const posts = (): Call[] => calls.filter((call) => call.method === 'POST');
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );

async function set(selector: string, value: string): Promise<void> {
  const node = field(selector);

  node.value = value;
  node.dispatchEvent(new Event('input'));
  await settle();
}

// The channel list is a searchable combobox (click opens it); the other two are the design-system Select.
async function openChoice(selector: string): Promise<void> {
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;

  const trigger = field(selector);

  trigger.dispatchEvent(
    new PointerEvent('pointerdown', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  trigger.click();
  await settle();
}

const choiceValues = (): string[] =>
  Array.from(document.body.querySelectorAll<HTMLElement>('[data-option]')).map(
    (node) => node.dataset.option ?? '',
  );

async function choose(selector: string, value: string): Promise<void> {
  await openChoice(selector);

  const option = document.body.querySelector<HTMLElement>(`[data-option="${value}"]`)!;

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

async function chooseScope(): Promise<void> {
  await choose('[data-tenant]', 'b1');
  await choose('[data-projection]', 'chat-history');
  await set('[data-from]', '2026-10-17T10:00');
  await set('[data-to]', '2026-10-17T11:00');
}

async function preview(): Promise<void> {
  field('[data-preview]').click();
  await settle();
}

describe('AdminEventReplayView', () => {
  it('offers the projections and channels the API returned, never a fixed list', async () => {
    await open();

    expect(document.body.querySelector('select')).toBeNull();

    await openChoice('[data-projection]');
    expect(choiceValues()).toContain('chat-history');

    await openChoice('[data-tenant]');

    const tenants = [...document.body.querySelectorAll('[data-option]')];

    expect(tenants.map((node) => node.textContent?.trim())).toContain('Alpha');
  });

  it('offers only the event types the chosen projection subscribes to', async () => {
    await open();
    await choose('[data-projection]', 'chat-history');
    await openChoice('[data-event-type]');

    expect(choiceValues()).toEqual(['all', 'chat.message', 'chat.cleared']);
  });

  it('counts the scope with a preview that replays nothing, and blocks the replay until then', async () => {
    await open();
    await chooseScope();

    expect(field('[data-replay]').disabled).toBe(true);

    await preview();

    const sent = posts();

    expect(sent).toHaveLength(1);
    expect(sent[0]!.path).toBe('/api/v{version}/admin/eventstore/replay/preview');

    expect(sent[0]!.options?.body).toMatchObject({
      broadcasterId: 'b1',
      projectionName: 'chat-history',
      eventType: null,
    });

    expect(text()).toContain('42 events');
    expect(field('[data-replay]').disabled).toBe(false);
  });

  it('shows the channel, projection and count in the confirm dialog before it runs, then sends the counted number', async () => {
    await open();
    await chooseScope();
    await preview();
    field('[data-replay]').click();
    await settle();

    const dialog = document.body.querySelector('[role="dialog"]')!.textContent ?? '';

    expect(dialog).toContain('42 events');
    expect(dialog).toContain('Alpha');
    expect(dialog).toContain('chat-history');
    expect(posts()).toHaveLength(1);

    byText('[role="dialog"] button', 'Replay events')!.click();
    await settle();

    const run = posts().at(-1)!;

    expect(run.path).toBe('/api/v{version}/admin/eventstore/replay/execute');

    expect(run.options?.body).toMatchObject({
      broadcasterId: 'b1',
      projectionName: 'chat-history',
      expectedCount: 42,
    });

    expect(text()).toContain('Replayed 42 events');
  });

  it('drops the count when the scope changes, so a replay never runs on a stale number', async () => {
    await open();
    await chooseScope();
    await preview();
    await set('[data-to]', '2026-10-17T12:00');

    expect(field('[data-replay]').disabled).toBe(true);
    expect(text()).not.toContain('42 events');
  });

  it('remembers the chosen projection across a remount', async () => {
    await open();
    await choose('[data-projection]', 'chat-history');

    expect(localStorage.getItem('nnz.adminEventReplay.projection')).toBe('chat-history');

    mounted?.unmount();
    await open();

    expect(field('[data-projection]').textContent).toContain('chat-history');
  });

  it('lists the executed replays from the audit log of the replay permission', async () => {
    await open();

    const history = calls.find((call) => call.path.endsWith('/admin/audit'))!;

    expect(history.options?.params?.query?.permission).toBe('eventstore:admin-replay');
    expect(document.body.querySelectorAll('[data-replay-row]')).toHaveLength(1);
    expect(text()).toContain('chat-history');
  });

  it('reloads when the hub says a replay finished', () => {
    const invalidate = vi.fn<(filter: { queryKey: string[] }) => void>();
    const handler = adminHandlers.find((entry) => entry.event === 'ReceiveEventReplayChanged')!;

    handler.handle(
      { queries: { invalidateQueries: invalidate } as unknown as QueryClient, channelId: null },
      {
        broadcasterId: 'b1',
        projectionName: 'chat-history',
        status: 'completed',
        processedCount: 42,
      } as never,
    );

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['admin', 'eventReplay'] });
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-event-replay');

    expect(registered?.path).toBe('admin/event-replay');
    expect(registered?.beforeEnter).toBe(dashboardAdminEventReplayRoute.beforeEnter);
  });
});
