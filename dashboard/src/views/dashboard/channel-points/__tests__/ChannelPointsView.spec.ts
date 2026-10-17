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

const baseReward = {
  id: 'r1',
  title: 'Hydrate',
  cost: 500,
  isEnabled: true,
  isManageable: true,
  isPaused: false,
  pipelineId: 'p1',
  response: null,
};

let reward: Record<string, unknown> = { ...baseReward };

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

  if (method === 'PATCH') {
    reward = { ...reward, ...(options?.body as Record<string, unknown>) };
  }

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
  reward = { ...baseReward };
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
    expect(row.textContent).toContain('500');
    expect(row.querySelector('[data-reward-enabled]')?.getAttribute('aria-checked')).toBe('true');
  });

  it('turns a reward off with the switch and the row shows it', async () => {
    const view = await open();

    await view.get('[data-reward-enabled]').trigger('click');
    await flushPromises();

    const patch = calls.find((call) => call.method === 'PATCH');

    expect(patch?.path).toContain('/rewards/{rewardId}');
    expect(patch?.body).toEqual({ isEnabled: false });
    expect(view.get('[data-reward-enabled]').attributes('aria-checked')).toBe('false');
  });

  it('keeps one Edit button per row and no inline cost field', async () => {
    const view = await open();

    expect(view.findAll('[data-reward-edit]')).toHaveLength(1);
    expect(view.find('[data-reward-cost]').exists()).toBe(false);
  });

  it('saves every field of the reward from the edit dialog', async () => {
    const view = await open();

    await view.get('[data-reward-edit]').trigger('click');
    await flushPromises();

    const field = (name: string): HTMLInputElement =>
      document.body.querySelector(`[data-field="${name}"]`) as HTMLInputElement;

    const type = async (name: string, value: string): Promise<void> => {
      field(name).value = value;
      field(name).dispatchEvent(new Event('input', { bubbles: true }));
      await flushPromises();
    };

    await type('title', 'Stay hydrated');
    await type('prompt', 'Drink up');
    await type('response', '{{user}} drank');
    await type('cost', '750');
    await type('backgroundColor', '#112233');
    await type('maxPerStream', '5');
    await type('maxPerUserPerStream', '2');
    await type('globalCooldownSeconds', '60');
    await type('timerDurationSeconds', '30');
    field('isEnabled').click();
    field('isPaused').click();
    field('isUserInputRequired').click();
    await flushPromises();

    (document.body.querySelector('[data-form-action]') as HTMLButtonElement).click();
    await flushPromises();

    const patch = calls.find((call) => call.method === 'PATCH');

    expect(patch?.body).toEqual({
      title: 'Stay hydrated',
      prompt: 'Drink up',
      response: '{{user}} drank',
      cost: 750,
      isEnabled: false,
      isPaused: true,
      isUserInputRequired: true,
      backgroundColor: '#112233',
      maxPerStream: 5,
      maxPerUserPerStream: 2,
      globalCooldownSeconds: 60,
      timerDurationSeconds: 30,
      pipelineId: 'p1',
    });
  });

  it('saves the color picked with the color picker', async () => {
    const view = await open();

    await view.get('[data-reward-edit]').trigger('click');
    await flushPromises();

    const swatch = document.body.querySelector('[data-color-swatch]') as HTMLInputElement;

    swatch.value = '#aa00ff';
    swatch.dispatchEvent(new Event('input', { bubbles: true }));
    await flushPromises();

    const text = document.body.querySelector('[data-field="backgroundColor"]') as HTMLInputElement;

    expect(text.value).toBe('#AA00FF');

    (document.body.querySelector('[data-form-action]') as HTMLButtonElement).click();
    await flushPromises();

    const patch = calls.find((call) => call.method === 'PATCH');

    expect(patch?.body).toMatchObject({ backgroundColor: '#AA00FF' });
  });

  it('disables the dialog fields of a reward the bot cannot manage', async () => {
    reward = { ...reward, isManageable: false };

    const view = await open();

    await view.get('[data-reward-edit]').trigger('click');
    await flushPromises();

    expect((document.body.querySelector('[data-field="title"]') as HTMLInputElement).disabled).toBe(true);
    const reason = en.dashboard.channelPoints.rewards.dialog.unmanageable;

    expect(document.body.textContent).toContain(reason);
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
    expect(view.get('[data-reward-edit]').element.closest('fieldset')?.disabled).toBe(true);
  });
});
