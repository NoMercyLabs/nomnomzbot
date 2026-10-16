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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import ChannelPointsView from '../ChannelPointsView.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
};

const calls: Call[] = [];
let held: string[] = [];

const ok = (data: unknown): { data: { data: unknown }; response: { status: number } } => ({
  data: { data },
  response: { status: 200 },
});

const reward = {
  id: 'r1',
  title: 'Hydrate',
  cost: 500,
  isEnabled: true,
  isManageable: true,
  isPaused: false,
  pipelineId: 'p1',
  response: null,
};

const redemption = (id: string, status: string): Record<string, unknown> => ({
  redemptionId: id,
  rewardId: 'r1',
  rewardTitle: 'Hydrate',
  userId: 'u1',
  userDisplayName: 'DukaSoft',
  cost: 500,
  status,
  redeemedAt: '2026-10-15T12:00:00Z',
});

function read(path: string): unknown {
  if (path.endsWith('/roles/effective/me'))
    return ok({ heldActionKeys: held });

  if (path.endsWith('/rewards/redemptions'))
    return ok([redemption('d1', 'unfulfilled'), redemption('d2', 'fulfilled')]);

  if (path.endsWith('/rewards'))
    return ok([reward]);

  if (path.endsWith('/pipelines'))
    return { data: { data: [{ id: 'p1', name: 'Drink water' }], hasMore: false }, response: { status: 200 } };

  return ok(null);
}

function write(method: string, path: string, options?: Options): unknown {
  calls.push({
    method,
    path,
    body: options?.body,
  });

  return { response: { status: 200 } };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => read(path),
    POST: async (path: string, options?: Options) => write('POST', path, options),
    PATCH: async (path: string, options?: Options) => write('PATCH', path, options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

async function open(): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: ChannelPointsView }],
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  await router.push('/');

  mounted = mount(ChannelPointsView, {
    global: {
      plugins: [i18n, router, [VueQueryPlugin, { queryClient }]],
    },
  });

  await flushPromises();

  return mounted;
}

beforeEach(() => {
  calls.length = 0;
  held = ['reward:read', 'reward:manage'];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal('ResizeObserver', class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('ChannelPointsView', () => {
  it('shows each reward with its cost, state and the pipeline it runs', async () => {
    const view = await open();
    const row = view.get('[data-reward-item]').element.parentElement!;

    expect(row.textContent).toContain('Hydrate');
    expect(row.textContent).toContain('Drink water');
    expect((row.querySelector('[data-reward-cost]') as HTMLInputElement).value).toBe('500');
    expect(row.querySelector('[data-reward-enabled]')?.getAttribute('aria-checked')).toBe('true');
  });

  it('sends only the new cost when the cost is changed', async () => {
    const view = await open();
    const input = view.get('[data-reward-cost]');

    (input.element as HTMLInputElement).value = '750';
    await input.trigger('change');
    await flushPromises();

    const patch = calls.find((call) => call.method === 'PATCH');

    expect(patch?.path).toContain('/rewards/{rewardId}');
    expect(patch?.body).toEqual({ cost: 750 });
  });

  it('lists only the waiting redemption and completes it', async () => {
    const view = await open();
    const rows = view.findAll('[data-redemption-item]');

    expect(rows).toHaveLength(1);
    expect(rows[0]!.text()).toContain('DukaSoft');

    await rows[0]!.get('[data-redemption-complete]').trigger('click');
    await flushPromises();

    const post = calls.find((call) => call.method === 'POST');

    expect(post?.path).toContain('/redemptions/{redemptionId}/fulfill');
  });

  it('hides the redemption actions from a role without reward:manage', async () => {
    held = ['reward:read'];

    const view = await open();

    expect(view.find('[data-redemption-complete]').exists()).toBe(false);
    expect(view.find('[data-redemption-reject]').exists()).toBe(false);
    expect(view.get('[data-reward-cost]').element.closest('fieldset')?.disabled).toBe(true);
  });
});
