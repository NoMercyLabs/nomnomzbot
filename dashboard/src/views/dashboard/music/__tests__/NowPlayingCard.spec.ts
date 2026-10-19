// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { NowPlaying } from '@/api/music';
import { i18n } from '@/i18n';

import NowPlayingCard from '../components/NowPlayingCard.vue';
import en from '../i18n/en.json';

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (): boolean => true }),
}));

let mounted: VueWrapper | undefined;

beforeEach(() => {
  vi.stubGlobal('ResizeObserver', class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  });
});

afterEach(() => {
  mounted?.unmount();
  vi.unstubAllGlobals();
});

const track = {
  trackName: 'Fuck iT ! I choose you',
  artist: 'Some Artist',
  requestedBy: 'iFiLLz',
  requesterId: 'u1',
  requesterColor: null,
  imageUrl: null,
  durationMs: 3_725_000,
  progressMs: 1000,
  volume: 50,
  isPlaying: true,
  repeatState: 'off',
} as unknown as NowPlaying;

function card(override: Partial<NowPlaying> = {}): VueWrapper {
  i18n.global.mergeLocaleMessage('en', en);

  mounted = mount(NowPlayingCard, {
    props: { track: { ...track, ...override }, observedAt: Date.now() },
    global: { plugins: [i18n, createPinia()] },
  });

  return mounted;
}

// Stoney 2026-10-18: "the breakpoints need to be better defined based on the other component's needs": the card
// answers to its own panel width (a container query), not to the window.
describe('the now playing card in a narrow or wide panel', () => {
  it('measures its own container and stacks the text above the progress row until the container is wide', () => {
    const wrapper = card();

    expect(wrapper.find('[data-player-box]').classes()).toContain('@container');

    const head: string[] = wrapper.find('[data-player-head]').classes();

    expect(head).toContain('flex-col');
    expect(head).toContain('@xl:flex-row');
  });

  it('never lets the head shrink into the progress row, so the requester line cannot overlap the bar', () => {
    const head: string[] = card().find('[data-player-head]').classes();

    expect(head).not.toContain('min-h-0');
    expect(head).toContain('flex-none');
  });

  it('keeps both time labels whole: they size to their text instead of a fixed 40px', () => {
    const labels = card().findAll('[data-player-time]');

    expect(labels).toHaveLength(2);

    expect(labels.map((label) =>
      label.text())).toEqual(['0:01', '62:05']);

    for (const label of labels) {
      expect(label.classes()).toContain('shrink-0');
      expect(label.classes()).not.toContain('w-10');
    }
  });

  it('puts the volume under the transport row until the container holds both beside it', () => {
    const row: string[] = card().find('[data-player-controls]').classes();

    expect(row).toContain('grid-cols-1');
    expect(row).toContain('@2xl:grid-cols-[1fr_auto_1fr]');
  });
});

// music-playback-determinism.md S-PD-11: the card shows what the director says, the same for both engines.
describe('the now playing card shows the director state', () => {
  it('says "Paused by you" when the owner paused, and nothing while the song plays', () => {
    expect(card({ state: 'pausedByOwner', isPlaying: false }).find('[data-player-state]').text()).toBe('Paused by you');
    expect(card({ state: 'playing' }).find('[data-player-state]').text()).toBe('');
  });

  it('says why a start is blocked and marks it as a problem', () => {
    const badge = card({ state: 'blocked', isPlaying: false }).find('[data-player-state]');

    expect(badge.text()).toBe('Blocked: the player cannot start');
    expect(badge.html()).toContain('text-destructive');
  });

  it('says a start is waiting to be seen while the director is starting', () => {
    expect(card({ state: 'starting', isPlaying: false }).find('[data-player-state]').text()).toBe('Starting');
  });

  it('names the volume the music level and shows a mute that keeps the song playing', () => {
    const wrapper = card({ muted: true });

    expect(wrapper.find('[data-player-volume]').attributes('aria-label')).toBe('Music volume');
    expect(wrapper.find('[data-player-muted]').text()).toBe('Music muted');
    expect(card({ muted: false }).find('[data-player-muted]').exists()).toBe(false);
  });
});
