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

import HappeningPanel from '../components/HappeningPanel.vue';
import en from '../i18n/en.json';

type Fed = {
  type: string;
  username: string | null;
  data: unknown;
};

let fed: Fed[] = [];

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({
      data: {
        data: fed.map((item, index) => ({
          id: `e${index}`,
          type: item.type,
          userId: null,
          username: item.username,
          data: JSON.stringify(item.data),
          timestamp: '2026-10-10T21:15:00',
        })),
      },
      response: { status: 200, ok: true },
    }),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1'), current: ref({ isLive: true }) }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function rowsFor(items: Fed[]): Promise<{ title: string; detail: string }[]> {
  fed = items;

  mounted = mount(HappeningPanel, {
    props: { live: true },
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        createRouter({ history: createMemoryHistory(), routes: [] }),
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();

  const nodes = [...document.body.querySelectorAll<HTMLElement>('[data-activity-row]')];

  return nodes.map((node) => ({
    title: node.querySelector('.font-medium')?.textContent ?? '',
    detail: node.querySelector('.font-medium + span')?.textContent ?? '',
  }));
}

describe('HappeningPanel names who', () => {
  it('names the shoutout target as the bold title on every shoutout shape the server writes', async () => {
    const rows = await rowsFor([
      {
        type: 'bot.shoutout.pending',
        username: null,
        data: { Target: { DisplayName: 'Jeroen' } },
      },
      {
        type: 'bot.shoutout.auto-pending',
        username: null,
        data: { DisplayName: 'Kitte' },
      },
      {
        type: 'channel.shoutout.create',
        username: null,
        data: { toDisplayName: 'Aaoa' },
      },
      {
        type: 'channel.shoutout.receive',
        username: null,
        data: { fromBroadcasterDisplayName: 'CptTree', viewerCount: 6 },
      },
    ]);

    expect(rows.map((row) => row.title)).toEqual(['Jeroen', 'Kitte', 'Aaoa', 'CptTree']);
    expect(rows[0]?.detail).toContain('Shoutout waiting');
    expect(rows[2]?.detail).toContain('Shoutout sent');
  });

  it('names the user on a channel event whose server username is empty', async () => {
    const rows = await rowsFor([
      {
        type: 'channel.ban',
        username: null,
        data: { targetDisplayName: 'Troll' },
      },
      {
        type: 'channel.subscription.gift',
        username: null,
        data: { gifterDisplayName: 'Gifter' },
      },
      {
        type: 'channel.follow',
        username: null,
        data: { userDisplayName: 'StanLeFresh' },
      },
    ]);

    expect(rows.map((row) => row.title)).toEqual(['Troll', 'Gifter', 'StanLeFresh']);
    expect(rows[2]?.detail).toContain('Followed');
  });

  it('keeps the generic title for an event that carries no name at all', async () => {
    const rows = await rowsFor([
      {
        type: 'channel.hype_train.begin',
        username: null,
        data: { level: 1 },
      },
    ]);

    expect(rows[0]?.title).toBe('Hype train started');
  });
});
