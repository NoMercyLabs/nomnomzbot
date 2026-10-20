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
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import type { NowPlaying } from '@/api/music';
import { i18n } from '@/i18n';

import PlayerButtons from '../components/PlayerButtons.vue';
import en from '../i18n/en.json';

type RunInit = {
  params: { path: { channelId: string } };
  body: object;
};

const held = vi.hoisted(() => ({
  keys: [] as string[],
  get: vi.fn<(path: string) => unknown>(),
  post: vi.fn<(path: string, init: RunInit) => unknown>(),
}));

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
    GET: held.get,
    POST: held.post,
    use: vi.fn<() => void>(),
  },
}));

const track = {
  trackName: 'Midnight City',
  artist: 'M83',
  trackUri: 'spotify:track:abc',
  requesterId: '42',
  requestedBy: 'DukaSoft',
} as unknown as NowPlaying;

const saved = [
  {
    id: 'b1',
    label: 'Thank them',
    icon: 'heart',
    actionType: 'send_message',
    parameters: { message: 'thanks' },
  },
  {
    id: 'b2',
    label: 'Save song',
    icon: 'bookmark-add',
    actionType: 'save_track',
    parameters: {},
  },
];

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.keys = [];

  held.get.mockResolvedValue({
    data: { data: saved },
    error: undefined,
    response: { status: 200 },
  });

  held.post.mockResolvedValue({
    data: { data: { success: true, variables: {} } },
    error: undefined,
    response: { status: 200 },
  });

  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  vi.clearAllMocks();
});

async function open(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(PlayerButtons, {
    props: { track },
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

describe('the player buttons under the now playing card', () => {
  it('shows the channel\'s saved buttons in their saved order', async () => {
    const view = await open(['music:queue:read']);
    const labels = view.findAll('[data-player-button]').map((b) => b.text());

    expect(labels).toEqual(['Thank them', 'Save song']);
  });

  it('runs the button action with the track and the requester as variables', async () => {
    const view = await open(['music:queue:read']);

    await view.findAll('[data-player-button]')[0]?.trigger('click');
    await flushPromises();

    expect(held.post).toHaveBeenCalledTimes(1);

    const [path, init] = held.post.mock.calls[0] ?? [];

    expect(path).toBe('/api/v{version}/channels/{channelId}/actions/run');
    expect(init?.params.path.channelId).toBe('c1');

    expect(init?.body).toEqual({
      actionType: 'send_message',
      parameters: { message: 'thanks' },
      variables: {
        'song.name': 'Midnight City',
        'song.artist': 'M83',
        'song.requester': 'DukaSoft',
        'target.id': '42',
        'target.displayname': 'DukaSoft',
      },
    });
  });

  it('is hidden without the queue read permission and never asks the API', async () => {
    const view = await open([]);

    expect(view.find('[data-player-buttons]').exists()).toBe(false);
    expect(held.get).not.toHaveBeenCalled();
  });
});
