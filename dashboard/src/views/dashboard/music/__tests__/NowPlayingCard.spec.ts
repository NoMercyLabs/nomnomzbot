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

function card(): VueWrapper {
  i18n.global.mergeLocaleMessage('en', en);

  mounted = mount(NowPlayingCard, {
    props: { track, observedAt: Date.now() },
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
