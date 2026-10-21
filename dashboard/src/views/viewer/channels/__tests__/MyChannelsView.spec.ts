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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import MyChannelsView from '../MyChannelsView.vue';
import en from '../i18n/en.json';

type Answer = { data?: unknown; response: { status: number; ok: boolean } };

let answers: Record<string, unknown> = {};
let hangReads = false;

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });

vi.mock('@/api/client', () => ({
  client: {
    GET: vi.fn(async (path: string) => {
      if (hangReads) {
        return new Promise<never>(() => undefined);
      }

      const match = Object.keys(answers).find((suffix) => path.endsWith(suffix));

      return match
        ? ok(answers[match])
        : { error: { status: 'error' }, response: { status: 404, ok: false } };
    }),
  },
}));

const watched = (name: string, isLive: boolean) => ({
  channelId: `id-${name}`,
  name,
  displayName: name.toUpperCase(),
  profileImageUrl: null,
  isLive,
  lastSeenAt: '2026-10-14T10:00:00Z',
}) as const satisfies Record<string, unknown>;

async function mountPage(): Promise<VueWrapper> {
  i18n.global.mergeLocaleMessage('en', en);
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/me', component: MyChannelsView },
      {
        path: '/me/:channel',
        name: 'viewer-me',
        component: MyChannelsView,
      },
      {
        path: '/app/u/:channelName',
        name: 'managed',
        component: MyChannelsView,
      },
    ],
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  await router.push('/me');
  const wrapper = mount(MyChannelsView, {
    global: { plugins: [router, [VueQueryPlugin, { queryClient }], i18n] },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  answers = {};
  hangReads = false;
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('MyChannelsView', () => {
  it('shows one card per watched channel, one primary Open on the first live one, linking to /me/<login>', async () => {
    answers = {
      '/me/watched-channels': [watched('quiet', false), watched('loud', true)],
      '/channels': [],
      '/channels/moderated': [],
    };

    const wrapper = await mountPage();

    const cards = wrapper.findAll('[data-viewer-channel]');

    expect(cards).toHaveLength(2);

    const primary = wrapper.findAll('[data-viewer-channel] a[data-variant="default"]');

    expect(primary).toHaveLength(1);
    expect(primary[0]!.attributes('href')).toBe('/me/loud');
    expect(cards[0]!.find('a').attributes('href')).toBe('/me/quiet');
    expect(cards[0]!.find('a').attributes('data-variant')).toBe('outline');
    expect(cards[1]!.text()).toContain('Live');
  });

  it('lists the channels the person manages in a second group that opens the management shell', async () => {
    answers = {
      '/me/watched-channels': [],
      '/channels': [{
        id: 'c1',
        login: 'mine',
        displayName: 'Mine',
        isLive: false,
      }],
      '/channels/moderated': [
        {
          id: 'c2',
          login: 'other',
          displayName: 'Other',
          isOnboarded: true,
        },
        {
          id: 'c1',
          login: 'mine',
          displayName: 'Mine',
          isOnboarded: true,
        },
      ],
    };

    const wrapper = await mountPage();

    const links = wrapper.findAll('[data-managed-channel] a').map((a) => a.attributes('href'));

    expect(links).toEqual(['/app/u/mine', '/app/u/other']);
  });

  it('shows the empty state when the person watches no channel', async () => {
    answers = {
      '/me/watched-channels': [],
      '/channels': [],
      '/channels/moderated': [],
    };

    const wrapper = await mountPage();

    expect(wrapper.find('[data-viewer-channel]').exists()).toBe(false);
    expect(wrapper.find('[data-viewer-channels-empty]').text()).toContain(en.viewer.channels.empty.title);
  });
});

describe('MyChannelsView loading', () => {
  it('shows placeholder cards while the read runs, and neither the empty state nor a card', async () => {
    hangReads = true;

    const wrapper = await mountPage();

    expect(wrapper.findAll('[data-viewer-channel-loading]')).toHaveLength(2);
    expect(wrapper.find('[data-viewer-channels-empty]').exists()).toBe(false);
    expect(wrapper.find('[data-viewer-channel]').exists()).toBe(false);
  });
});
