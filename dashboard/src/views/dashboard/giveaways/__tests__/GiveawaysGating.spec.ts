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

import GiveawaysView from '../GiveawaysView.vue';
import en from '../i18n/en.json';

const giveaway = {
  id: 'g1',
  title: 'Keyboard',
  status: 'closed',
  entryCount: 3,
  entryMode: 'keyword',
  keyword: '!enter',
  winnerCount: 1,
  maxEntriesPerUser: 1,
  excludeModerators: false,
  prizeMode: 'announce',
  prizeFromPot: false,
  requires18Plus: false,
  createdAt: '2026-10-15T18:00:00Z',
};

const winner = {
  id: 'w1',
  viewerDisplayName: 'DukaSoft',
  status: 'pending',
  isRedraw: false,
};
const entry = {
  id: 'e1',
  viewerDisplayName: 'Alice',
  ticketCount: 2,
};
const pool = {
  id: 'p1',
  name: 'Steam keys',
  total: 5,
  available: 4,
  assigned: 1,
};

const held = vi.hoisted(() => ({ keys: [] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => held.keys.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      const byPath: Record<string, unknown> = {
        '/api/v{version}/giveaways/{id}/winners': [winner],
        '/api/v{version}/giveaways/{id}/entries': [entry],
        '/api/v{version}/giveaways/code-pools': [pool],
      };

      return {
        data: { data: byPath[path] ?? [giveaway] },
        response: { status: 200, ok: true },
      };
    },
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.keys = [];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(GiveawaysView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

describe('GiveawaysView shows only what the person may use', () => {
  it('shows no list, winners, entries, pools or action without any key', async () => {
    const view = await open([]);

    expect(view.find('[data-giveaway-row]').exists()).toBe(false);
    expect(view.find('[data-winner-row]').exists()).toBe(false);
    expect(view.find('[data-entry-row]').exists()).toBe(false);
    expect(view.find('[data-pool-row]').exists()).toBe(false);
    expect(view.find('[data-giveaway-new]').exists()).toBe(false);
  });

  it('shows the reads but no write control with giveaways:read alone', async () => {
    const view = await open(['giveaways:read']);

    expect(view.find('[data-giveaway-row]').exists()).toBe(true);
    expect(view.find('[data-winner-row]').exists()).toBe(true);
    expect(view.find('[data-entry-row]').exists()).toBe(true);
    expect(view.find('[data-giveaway-new]').exists()).toBe(false);
    expect(view.find('[data-giveaway-action]').exists()).toBe(false);
    expect(view.find('[data-giveaway-edit]').exists()).toBe(false);
    expect(view.find('[data-giveaway-delete]').exists()).toBe(false);
    expect(view.find('[data-winner-redraw]').exists()).toBe(false);
    expect(view.find('[data-pool-row]').exists()).toBe(false);
  });

  it('shows every write control with giveaways:write', async () => {
    const view = await open(['giveaways:read', 'giveaways:write']);

    expect(view.find('[data-giveaway-new]').exists()).toBe(true);
    expect(view.find('[data-giveaway-action]').exists()).toBe(true);
    expect(view.find('[data-giveaway-edit]').exists()).toBe(true);
    expect(view.find('[data-giveaway-delete]').exists()).toBe(true);
    expect(view.find('[data-winner-redraw]').exists()).toBe(true);
  });

  it('shows the code pools only with giveaways:codes:write', async () => {
    const view = await open(['giveaways:read', 'giveaways:codes:write']);

    expect(view.find('[data-pool-row]').exists()).toBe(true);
  });
});
