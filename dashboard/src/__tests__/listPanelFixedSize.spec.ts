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

import RecentFollowers from '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/recent_followers.vue';
import SrQueue from '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/sr_queue.vue';
import TopCheerers from '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/top_cheerers.vue';

// A list panel keeps its full size (all `count` rows) at all times, so its anchor holds it in the same place in
// OBS when a song, follower or cheerer is added or removed (Stoney 2026-11-26: "i need all the boxes to be on
// it's max size so they stay in the right position").

type Handler = (data: unknown) => void;

const handlers: Record<string, Handler> = {};
const settingsListeners: ((settings: Record<string, unknown>) => void)[] = [];
const wrappers: VueWrapper[] = [];

const sdk = {
  onSettings: (cb: (settings: Record<string, unknown>) => void) => {
    settingsListeners.push(cb);

    return sdk;
  },
  on: (type: string, cb: Handler) => {
    handlers[type] = cb;

    return sdk;
  },
  off: (type: string) => {
    delete handlers[type];

    return sdk;
  },
};

function mountWithCount(widget: typeof SrQueue, count: number): VueWrapper {
  const wrapper = mount(widget);

  wrappers.push(wrapper);
  settingsListeners.forEach((cb) => cb({ count }));

  return wrapper;
}

function rows(wrapper: VueWrapper) {
  return {
    all: wrapper.findAll('.nnz-row').length,
    pads: wrapper.findAll('.nnz-row.nnz-pad').length,
  } as const satisfies { all: number; pads: number };
}

function song(n: number): Record<string, unknown> {
  return {
    title: `Song ${n}`,
    requestedBy: 'Viewer',
    durationSec: 180,
    number: n,
    code: '',
  };
}

beforeEach(() => {
  settingsListeners.splice(0);
  vi.stubGlobal('NomNomz', sdk);
});

afterEach(() => {
  wrappers.splice(0).forEach((w) => w.unmount());
  vi.unstubAllGlobals();
});

describe('a list panel always draws its full row count', () => {
  it('the song request queue keeps 5 rows with 1 song and with 3 songs', async () => {
    const wrapper = mountWithCount(SrQueue, 5);

    handlers.sr_queue!({ items: [song(1)] });
    await wrapper.vm.$nextTick();
    expect(rows(wrapper)).toEqual({ all: 5, pads: 4 });

    handlers.sr_queue!({ items: [song(1), song(2), song(3)] });
    await wrapper.vm.$nextTick();
    expect(rows(wrapper)).toEqual({ all: 5, pads: 2 });
  });

  it('the song request queue shows no filler past its count', async () => {
    const wrapper = mountWithCount(SrQueue, 2);

    handlers.sr_queue!({ items: [song(1), song(2), song(3)] });
    await wrapper.vm.$nextTick();
    expect(rows(wrapper)).toEqual({ all: 2, pads: 0 });
  });

  it('recent followers keeps 4 rows when empty and after one follow', async () => {
    const wrapper = mountWithCount(RecentFollowers, 4);

    await wrapper.vm.$nextTick();
    expect(rows(wrapper)).toEqual({ all: 4, pads: 3 });
    expect(wrapper.find('.nnz-row.nnz-sub').text()).toBe('Nobody yet. Be the first.');

    handlers.follow!({ displayName: 'NewFollower' });
    await wrapper.vm.$nextTick();
    expect(rows(wrapper)).toEqual({ all: 4, pads: 3 });
    expect(wrapper.find('.nnz-row .nnz-grow').text()).toBe('NewFollower');
  });

  it('top cheerers keeps 3 rows when empty and after two cheerers', async () => {
    const wrapper = mountWithCount(TopCheerers, 3);

    await wrapper.vm.$nextTick();
    expect(rows(wrapper)).toEqual({ all: 3, pads: 2 });

    handlers.cheer!({ displayName: 'A', bits: 100 });
    handlers.cheer!({ displayName: 'B', bits: 50 });
    await wrapper.vm.$nextTick();
    expect(rows(wrapper)).toEqual({ all: 3, pads: 1 });
  });
});
