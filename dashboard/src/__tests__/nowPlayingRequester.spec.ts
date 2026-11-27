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

import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import NowPlaying from '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/now_playing.vue';

type Handler = (data: unknown) => void;

const handlers: Record<string, Handler> = {};
const wrappers: VueWrapper[] = [];

const sdk = {
  onSettings: () => sdk,
  on: (type: string, cb: Handler) => {
    handlers[type] = cb;

    return sdk;
  },
  off: (type: string) => {
    delete handlers[type];

    return sdk;
  },
  data: { nowPlaying: () => Promise.resolve(null) },
  image: (url: string) => url,
};

beforeEach(() => {
  vi.stubGlobal('NomNomz', sdk);

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
  wrappers.splice(0).forEach((w) => w.unmount());
  vi.unstubAllGlobals();
});

describe('the now playing card names the requester', () => {
  it('starts the requester line with a capital letter', async () => {
    const wrapper = mount(NowPlaying);

    wrappers.push(wrapper);

    handlers['music.nowPlaying']!({
      engine: 'spotify',
      title: 'Fly Away',
      artists: ['Tones And I'],
      isPlaying: true,
      requestedBy: 'Stoney_Eagle',
      durationMs: 180000,
      progressMs: 1000,
    });

    await wrapper.vm.$nextTick();

    expect(wrapper.find('.content .requester').text().replace(/\s+/g, ' ')).toBe('Requested by Stoney_Eagle');
  });
});
