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
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { i18n } from '@/i18n';

import MeView from '../MeView.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, string> } };
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const requested: string[] = [];
let answers: Record<string, () => Answer> = {};

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });
const refused = (status: number): Answer => ({
  error: { status: 'error' },
  response: { status, ok: false },
});

const summary = {
  firstSeenAt: '2026-01-02T10:00:00Z',
  lastSeenAt: '2026-10-14T10:00:00Z',
  totalMessages: 1204,
  totalCommandsUsed: 37,
  totalSongRequests: 9,
  pointsEnabled: true,
  balance: 560,
  songRequestsEnabled: true,
  followedAt: '2026-02-03T10:00:00Z',
  subTier: '1000',
  isLive: true,
};

const song = (position: number, trackName: string): Record<string, unknown> => ({
  position,
  trackName,
  artist: 'M83',
  imageUrl: null,
  durationMs: 200000,
  code: `c${position}`,
});

vi.mock('@/api/client', () => ({
  client: {
    GET: vi.fn(async (path: string, options?: Options) => {
      requested.push(`${path} ${JSON.stringify(options?.params?.path ?? {})}`);
      const match = Object.keys(answers).find((suffix) => path.endsWith(suffix));

      return match
        ? answers[match]!()
        : refused(404);
    }),
  },
}));

function freshAnswers(): Record<string, () => Answer> {
  return {
    '/public/sr/by-channel/{channelName}': () =>
      ok({
        broadcasterId: 'chan-1',
        channelName: 'Stoney_Eagle',
        isAcceptingRequests: true,
        enabledProviders: ['spotify'],
      }),
    '/viewer/me': () => ok(summary),
    '/viewer/me/queue': () =>
      ok({
        queueLength: 6,
        songs: [song(1, 'Midnight City'), song(4, 'Strobe')],
      }),
  };
}

async function mountPage(): Promise<VueWrapper> {
  i18n.global.mergeLocaleMessage('en', en);
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/me/:channel', component: MeView }],
  });

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  await router.push('/me/stoney_eagle');
  const wrapper: VueWrapper = mount(MeView, {
    global: {
      plugins: [
        router,
        [VueQueryPlugin, { queryClient }],
        i18n,
      ],
    },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  requested.length = 0;
  answers = freshAnswers();
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('the viewer page', () => {
  it('resolves the channel name to its id and reads the viewer tiles from that channel', async () => {
    const wrapper = await mountPage();

    expect(requested[0]).toContain('"channelName":"stoney_eagle"');
    expect(requested.some((r) => r.includes('/viewer/me {') && r.includes('"channelId":"chan-1"'))).toBe(true);

    const tiles = wrapper.get('[data-viewer-stats]').text();

    expect(tiles).toContain('Messages');
    expect(tiles).toContain('1,204');
    expect(tiles).toContain('Commands used');
    expect(tiles).toContain('37');
    expect(tiles).toContain('Points');
    expect(tiles).toContain('560');
    expect(tiles).toContain('Tier 1000');
    expect(tiles).toContain('Live now');
    expect(wrapper.text()).toContain('Your stats in Stoney_Eagle');
  });

  it('lists only the viewer songs with their place in line', async () => {
    const wrapper = await mountPage();
    const queue = wrapper.get('[data-viewer-queue]').text();

    expect(queue).toContain('Midnight City');
    expect(queue).toContain('Next in line');
    expect(queue).toContain('Strobe');
    expect(queue).toContain('4 in line');
    expect(queue).toContain('6 songs in the whole queue');
  });

  it('hides the points tile when the channel has no points', async () => {
    answers['/viewer/me'] = () => ok({
      ...summary,
      pointsEnabled: false,
      balance: null,
    });

    const wrapper = await mountPage();

    expect(wrapper.get('[data-viewer-stats]').text()).not.toContain('Points');
  });

  it('says so when the viewer has no songs queued', async () => {
    answers['/viewer/me/queue'] = () => ok({ queueLength: 3, songs: [] });
    const wrapper = await mountPage();

    expect(wrapper.get('[data-viewer-queue]').text()).toContain('You have no songs in the queue.');
  });

  it('shows the not-set-up message and asks for no viewer data when the channel is unknown', async () => {
    answers['/public/sr/by-channel/{channelName}'] = () => refused(404);
    const wrapper = await mountPage();

    expect(wrapper.text()).toContain('This channel is not set up here.');
    expect(requested.some((r) => r.includes('/viewer/me'))).toBe(false);
  });
});
