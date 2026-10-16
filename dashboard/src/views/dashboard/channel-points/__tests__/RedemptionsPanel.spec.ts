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

import { i18n } from '@/i18n';
import { useSaveErrorsStore } from '@/stores/saveErrors';

import RedemptionsPanel from '../components/RedemptionsPanel.vue';
import en from '../i18n/en.json';

type Call = {
  method: string;
  path: string;
  redemptionId: unknown;
};
type PostOptions = { params?: { path?: { redemptionId?: string } } };

const calls: Call[] = [];
let held: string[] = [];
let failPosts = false;

const ok = (data: unknown): { data: { data: unknown }; response: { status: number } } => ({
  data: { data },
  response: { status: 200 },
});

const rewards = [
  {
    id: 'r1',
    title: 'Hydrate',
    cost: 500,
    backgroundColor: '#ff0066',
    imageUrl: 'https://cdn/r1.png',
  },
  {
    id: 'r2',
    title: 'Text to speech',
    cost: 100,
    backgroundColor: '#3366ff',
    imageUrl: null,
  },
];

const redemption = (
  id: string,
  rewardId: string,
  status: string,
  userInput?: string,
): Record<string, unknown> => ({
  redemptionId: id,
  rewardId,
  rewardTitle: rewardId === 'r1'
    ? 'Hydrate'
    : 'Text to speech',
  userId: `u-${id}`,
  userDisplayName: `Viewer ${id}`,
  cost: 500,
  userInput,
  status,
  redeemedAt: '2026-10-15T12:00:00Z',
});

const freshQueue = (): Record<string, unknown>[] => [
  redemption('d1', 'r1', 'unfulfilled', 'drink a whole glass'),
  redemption('d2', 'r1', 'fulfilled'),
  redemption('d3', 'r2', 'canceled'),
  redemption('d4', 'r1', 'unfulfilled'),
];

// Twitch decides the new status, so the fake box applies it the way Twitch would.
let queue = freshQueue();

function applyOnTwitch(path: string, redemptionId: string | undefined): void {
  const row = queue.find((entry) => entry.redemptionId === redemptionId);

  if (row) {
    row.status = path.endsWith('/fulfill')
      ? 'fulfilled'
      : 'canceled';
  }
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      if (path.endsWith('/roles/effective/me'))
        return ok({ heldActionKeys: held });

      if (path.endsWith('/rewards/redemptions'))
        return ok(queue);

      return ok(rewards);
    },
    POST: async (path: string, options?: PostOptions) => {
      calls.push({
        method: 'POST',
        path,
        redemptionId: options?.params?.path?.redemptionId,
      });

      if (failPosts)
        return { error: { title: 'Twitch refused' }, response: { status: 500 } };

      applyOnTwitch(path, options?.params?.path?.redemptionId);

      return { response: { status: 200 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

async function open(): Promise<VueWrapper> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(RedemptionsPanel, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

beforeEach(() => {
  calls.length = 0;
  localStorage.clear();
  queue = freshQueue();
  failPosts = false;
  held = ['reward:read', 'reward:manage'];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

function rowIds(view: VueWrapper): string[] {
  return view.findAll('[data-redemption-item]').map((row) =>
    row.attributes('data-redemption-item') ?? '');
}

async function press(view: VueWrapper, selector: string): Promise<void> {
  await view.get(selector).trigger('click');
  await flushPromises();
}

describe('RedemptionsPanel', () => {
  it('lists All with the pending total, then every reward with its own pending count, even at zero', async () => {
    const view = await open();
    const filters = view.findAll('[data-redemption-filter]');
    const summary = filters.map((filter) => [
      filter.attributes('data-redemption-filter'),
      filter.get('[data-redemption-count]').text(),
    ]);

    expect(summary).toEqual([
      ['all', '2'],
      ['r1', '2'],
      ['r2', '0'],
    ]);

    expect(filters[1]!.text()).toContain('Hydrate');
    expect(filters[2]!.text()).toContain('Text to speech');
  });

  it('shows only the unfulfilled requests, with the viewer text, and narrows by the chosen reward', async () => {
    const view = await open();

    expect(rowIds(view)).toEqual(['d1', 'd4']);
    expect(view.text()).toContain('drink a whole glass');

    await press(view, '[data-redemption-filter="r2"]');

    expect(rowIds(view)).toEqual([]);
    expect(view.text()).toContain(en.dashboard.channelPoints.redemptions.emptyFilter);

    await press(view, '[data-redemption-filter="r1"]');

    expect(rowIds(view)).toEqual(['d1', 'd4']);
  });

  it('completes one request: posts fulfill and the row leaves the pending list at once', async () => {
    const view = await open();

    await press(view, '[data-redemption-item="d1"] [data-redemption-complete]');

    expect(calls.map((call) => [call.path, call.redemptionId])).toEqual([
      ['/api/v{version}/channels/{channelId}/rewards/redemptions/{redemptionId}/fulfill', 'd1'],
    ]);

    expect(rowIds(view)).toEqual(['d4']);
    expect(view.get('[data-redemption-filter="all"] [data-redemption-count]').text()).toBe('1');
  });

  it('rejects the selected requests only after the dialog is confirmed, and refunds each', async () => {
    const view = await open();

    await press(view, '[data-redemption-select-all]');
    await press(view, '[data-redemption-bulk-reject]');

    expect(calls).toHaveLength(0);

    const confirm = document.body.querySelector('[data-confirm-action]') as HTMLElement;

    confirm.click();
    await flushPromises();

    expect(calls.map((call) => [call.path.split('/').pop(), call.redemptionId])).toEqual([
      ['refund', 'd1'],
      ['refund', 'd4'],
    ]);

    expect(rowIds(view)).toEqual([]);
  });

  it('completes every selected request in bulk', async () => {
    const view = await open();

    await press(view, '[data-redemption-item="d4"] [data-redemption-select]');
    await press(view, '[data-redemption-bulk-complete]');

    expect(calls.map((call) => [call.path.split('/').pop(), call.redemptionId])).toEqual([
      ['fulfill', 'd4'],
    ]);

    expect(rowIds(view)).toEqual(['d1']);
  });

  it('puts the row back and reports the failure when Twitch refuses', async () => {
    failPosts = true;
    const view = await open();

    await press(view, '[data-redemption-item="d1"] [data-redemption-complete]');

    expect(rowIds(view)).toEqual(['d1', 'd4']);
    expect(useSaveErrorsStore().items).toHaveLength(1);
  });

  it('remembers the chosen reward filter when the panel is opened again', async () => {
    const first = await open();

    await press(first, '[data-redemption-filter="r2"]');
    first.unmount();

    const again = await open();

    expect(again.get('[data-redemption-filter="r2"]').attributes('aria-pressed')).toBe('true');
  });

  it('gives a role without reward:manage the list but no actions', async () => {
    held = ['reward:read'];
    const view = await open();

    expect(rowIds(view)).toEqual(['d1', 'd4']);
    expect(view.find('[data-redemption-complete]').exists()).toBe(false);
    expect(view.find('[data-redemption-reject]').exists()).toBe(false);
    expect(view.find('[data-redemption-bulk-complete]').exists()).toBe(false);
  });
});
