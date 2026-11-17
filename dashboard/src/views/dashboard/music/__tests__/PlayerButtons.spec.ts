// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
  toast: vi.fn<(message: string) => void>(),
}));

vi.mock('vue-sonner', () => ({
  toast: { error: held.toast },
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

let queryClient = new QueryClient();

async function open(keys: string[], playing: NowPlaying | null = track): Promise<VueWrapper> {
  held.keys = keys;

  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(PlayerButtons, {
    props: { track: playing },
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

  it('shows a button with no label as its icon, named by its action', async () => {
    held.get.mockResolvedValue({
      data: {
        data: [
          {
            id: 'b3',
            label: '',
            icon: 'next',
            actionType: 'music_next',
            parameters: {},
          },
        ],
      },
      error: undefined,
      response: { status: 200 },
    });

    const view = await open(['music:queue:read']);
    const button = view.find('[data-player-button]');

    expect(button.text()).toBe('');
    expect(button.attributes('aria-label')).toBe('Next Song');
    expect(button.attributes('title')).toBe('Next Song');
    expect(button.find('svg').exists()).toBe(true);
  });

  it('keeps the label as the visible text and adds no aria-label when a label is set', async () => {
    const view = await open(['music:queue:read']);
    const button = view.find('[data-player-button]');

    expect(button.text()).toBe('Thank them');
    expect(button.attributes('aria-label')).toBeUndefined();
  });

  it('names the failed button by its action when it has no label', async () => {
    held.get.mockResolvedValue({
      data: {
        data: [
          {
            id: 'b3',
            label: '',
            icon: 'next',
            actionType: 'music_next',
            parameters: {},
          },
        ],
      },
      error: undefined,
      response: { status: 200 },
    });

    held.post.mockRejectedValue(new Error('down'));

    const view = await open(['music:queue:read']);

    await view.find('[data-player-button]').trigger('click');
    await flushPromises();

    expect(held.toast).toHaveBeenCalledWith('Next Song did not run.');
  });

  // Stoney 2026-11-17, on the Favorite button: "this button should be the reactive button as it is on the stream
  // deck and respond to true or false".
  describe('a button with a live state, as on the Stream Deck', () => {
    function oneButton(actionType: string, icon: string): void {
      held.get.mockResolvedValue({
        data: { data: [{
          id: 'live',
          label: 'Live',
          icon,
          actionType,
          parameters: {},
        }] },
        error: undefined,
        response: { status: 200 },
      });
    }

    async function liveButton(
      actionType: string,
      icon: string,
      state: Partial<NowPlaying>,
    ): Promise<{ pressed: string | undefined; icon: string | undefined }> {
      oneButton(actionType, icon);

      const view = await open(['music:queue:read'], { ...track, ...state });
      const button = view.find('[data-player-button]');

      return {
        pressed: button.attributes('aria-pressed'),
        icon: button.find('[data-player-icon]').attributes('data-player-icon'),
      };
    }

    it.each([
      ['music_toggle_saved', 'heart-filled', { isSaved: true }, 'true', 'heart-filled'],
      ['music_toggle_saved', 'heart-filled', { isSaved: false }, 'false', 'heart'],
      ['music_toggle_shuffle', 'shuffle', { shuffleState: true }, 'true', 'shuffle'],
      ['music_toggle_shuffle', 'shuffle', { shuffleState: false }, 'false', 'shuffle'],
      ['music_cycle_repeat', 'repeat-one', { repeatState: 'track' }, 'true', 'repeat-one'],
      ['music_cycle_repeat', 'repeat-one', { repeatState: 'context' }, 'true', 'repeat'],
      ['music_cycle_repeat', 'repeat-one', { repeatState: 'off' }, 'false', 'repeat'],
      ['music_volume_mute', 'volume-muted', { muted: true }, 'true', 'volume-muted'],
      ['music_volume_mute', 'volume-muted', { muted: false }, 'false', 'volume-two'],
    ] as const)('%s with %o shows pressed %s and the %s icon', async (actionType, icon, state, pressed, shown) => {
      const live = await liveButton(actionType, icon, state);

      expect(live).toEqual({ pressed, icon: shown });
    });

    it('shows play/pause as pause while the song plays and as play while it does not', async () => {
      expect(await liveButton('music_play_pause', 'play-circle', { isPlaying: true }))
        .toEqual({ pressed: undefined, icon: 'pause' });

      mounted?.unmount();

      expect(await liveButton('music_play_pause', 'play-circle', { isPlaying: false }))
        .toEqual({ pressed: undefined, icon: 'play' });
    });

    it('keeps an ordinary button with no live state unpressed and on its own icon', async () => {
      const view = await open(['music:queue:read']);
      const button = view.find('[data-player-button]');

      expect(button.attributes('aria-pressed')).toBeUndefined();
      expect(button.find('[data-player-icon]').attributes('data-player-icon')).toBe('heart');
    });

    it('reads the queue again after a press, so the button shows the new state', async () => {
      oneButton('music_toggle_saved', 'heart-filled');

      const view = await open(['music:queue:read'], { ...track, isSaved: false });
      const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

      await view.find('[data-player-button]').trigger('click');
      await flushPromises();

      expect(invalidate).toHaveBeenCalledWith({ queryKey: ['channels', 'c1', 'music-queue'] });
    });
  });

  it('is hidden without the queue read permission and never asks the API', async () => {
    const view = await open([]);

    expect(view.find('[data-player-buttons]').exists()).toBe(false);
    expect(held.get).not.toHaveBeenCalled();
  });
});
