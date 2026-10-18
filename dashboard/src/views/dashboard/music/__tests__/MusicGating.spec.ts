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

import type { NowPlaying, QueueItem } from '@/api/music';
import { i18n } from '@/i18n';

import NowPlayingCard from '../components/NowPlayingCard.vue';
import RequesterBulkRemove from '../components/RequesterBulkRemove.vue';
import en from '../i18n/en.json';

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

vi.mock('@/components/layout/UserName.vue', () => ({
  default: { template: '<span />' },
}));

const track = {
  trackName: 'Midnight City',
  artist: 'M83',
  album: 'Hurry Up',
  imageUrl: null,
  durationMs: 240000,
  progressMs: 60000,
  isPlaying: true,
  volume: 40,
  requestedBy: 'DukaSoft',
  provider: 'spotify',
  shuffleState: false,
  repeatState: 'off',
  canSetShuffle: true,
  canSetRepeat: true,
  canSkipNext: true,
  canSkipPrevious: true,
  canSeek: true,
  canPause: true,
  canResume: true,
} as unknown as NowPlaying;

const song: QueueItem = {
  position: 1,
  number: 1,
  trackName: 'Strobe',
  artist: 'deadmau5',
  imageUrl: null,
  durationMs: 1000,
  requestedBy: 'Bamo16',
  cost: 0,
};

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.keys = [];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function openCard(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  mounted = mount(NowPlayingCard, {
    attachTo: document.body,
    props: { track, observedAt: Date.now() },
    global: { plugins: [i18n] },
  });

  await flushPromises();

  return mounted;
}

async function openBulkRemove(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(RequesterBulkRemove, {
    attachTo: document.body,
    props: { rows: [song] },
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

const labeled = (view: VueWrapper, label: string): boolean =>
  view.find(`[aria-label="${label}"]`).exists();

describe('music shows only what the person may use', () => {
  it('shows the song but no transport, shuffle or volume control without a key', async () => {
    const view = await openCard([]);

    expect(view.text()).toContain('Midnight City');
    expect(labeled(view, en.dashboard.music.now.pause)).toBe(false);
    expect(labeled(view, en.dashboard.music.now.previous)).toBe(false);
    expect(labeled(view, en.dashboard.music.now.shuffle)).toBe(false);
    expect(labeled(view, en.dashboard.music.now.volume)).toBe(false);
  });

  it('shows pause and skip with music:queue:moderate but keeps shuffle and volume hidden', async () => {
    const view = await openCard(['music:queue:moderate']);

    expect(labeled(view, en.dashboard.music.now.pause)).toBe(true);
    expect(labeled(view, en.dashboard.music.now.previous)).toBe(true);
    expect(labeled(view, en.dashboard.music.now.shuffle)).toBe(false);
    expect(labeled(view, en.dashboard.music.now.volume)).toBe(false);
  });

  it('shows shuffle and volume with music:remote:control', async () => {
    const view = await openCard(['music:remote:control']);

    expect(labeled(view, en.dashboard.music.now.shuffle)).toBe(true);
    expect(labeled(view, en.dashboard.music.now.volume)).toBe(true);
    expect(labeled(view, en.dashboard.music.now.pause)).toBe(false);
  });

  it('offers the remove-by-requester row only with music:queue:moderate', async () => {
    const reader = await openBulkRemove([]);

    expect(reader.find('[data-requester-remove]').exists()).toBe(false);

    reader.unmount();
    mounted = null;

    const moderator = await openBulkRemove(['music:queue:moderate']);

    expect(moderator.find('[data-requester-remove]').exists()).toBe(true);
  });
});
