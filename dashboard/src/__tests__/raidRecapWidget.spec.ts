// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import RaidRecap from '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/raid_recap.vue';

// The widget is an overlay SFC served by the bot, not a dashboard component. It talks to the typed global
// `NomNomz` the overlay host injects. This stub is that host: it keeps the handlers the widget registers.
type Handler = (data: unknown) => void;

const handlers: Record<string, Handler> = {};
let settingsHandler: Handler = () => {};

const sdk = {
  onSettings: (cb: Handler): void => {
    settingsHandler = cb;
  },
  on: (type: string, cb: Handler): void => {
    handlers[type] = cb;
  },
  off: (type: string): void => {
    delete handlers[type];
  },
  data: {
    streamStats: vi.fn<(options: Record<string, unknown>) => Promise<unknown>>(),
  },
};

// The answer of nnz.data.streamStats: the stream window plus the metric keys (StreamStatsDto).
const fixture = {
  stream: {
    id: 's1',
    startedAt: '2026-10-15T11:59:00Z',
    endedAt: '2026-10-16T02:00:00Z',
    title: 'Coding and tunes',
    game: 'Software and Game Development',
  },
  duration: 50460,
  peakViewers: 36,
  followers: 3,
  subs: {
    new: 2,
    resub: 1,
    gift: 4,
    total: 7,
  },
  gifters: [
    { name: 'iFiLLz', count: 11 },
    { name: 'finwo_', count: 10 },
  ],
  selfSubs: ['kani_dev'],
  bits: { total: 200, top: [{ name: 'iFiLLz', count: 200 }] },
  raidsIn: [
    { name: 'MayaRainer', viewers: 49 },
    { name: 'theCodeDrift', viewers: 23 },
  ],
  redemptions: { total: 34, top: [{ name: 'Hydrate!', count: 6 }] },
  songRequesters: { total: 206, top: [{ name: 'finwo_', count: 86 }] },
  chat: { messages: 2123, uniqueChatters: 46 },
  topChatters: [{ name: 'iFiLLz', count: 436 }],
  commandsRun: { total: 340, top: [{ name: '!hug', count: 120 }] },
};

const wrappers: VueWrapper[] = [];

function show(data: unknown, settings: Record<string, unknown> = {}): VueWrapper {
  sdk.data.streamStats.mockResolvedValue(data);
  const wrapper = mount(RaidRecap);

  wrappers.push(wrapper);
  settingsHandler(settings);
  handlers['raid_recap.show']!({ stream: { id: 's1' } });

  return wrapper;
}

function withOverflow(box: number, list: number): void {
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, value: box });
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, value: list });
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal('NomNomz', sdk);
  withOverflow(0, 0);
});

afterEach(() => {
  wrappers.splice(0).forEach((w: VueWrapper) => w.unmount());
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('raid_recap widget', () => {
  it('renders every block from a full recap', async () => {
    const wrapper = show(fixture);

    await flushPromises();
    const blocks: string[] = wrapper.findAll('[data-block]').map((b) => b.attributes('data-block') ?? '');

    expect(blocks).toEqual(
      expect.arrayContaining([
        'duration',
        'peak',
        'messages',
        'commands',
        'subs',
        'raids',
        'chatters',
        'songs',
        'bits',
        'followers',
        'redemptions',
        'topCommands',
      ]),
    );

    expect(wrapper.find('[data-block="peak"]').text()).toContain('36');
    expect(wrapper.find('[data-block="subs"]').text()).toContain('iFiLLz');
    expect(wrapper.find('[data-block="subs"]').text()).toContain('kani_dev');
    expect(wrapper.find('[data-block="raids"]').text()).toContain('MayaRainer');
  });

  it('reads the shown stream through data.streamStats with the metrics setting', async () => {
    show(fixture, { metrics: ['followers', 'raidsIn'] });

    await flushPromises();
    expect(sdk.data.streamStats).toHaveBeenLastCalledWith({ stream: 's1', metrics: ['followers', 'raidsIn'] });
  });

  it('reflows a quiet stream: zero cards are gone and the rest fill the row', async () => {
    const quiet = {
      stream: fixture.stream,
      duration: 3600,
      followers: 2,
      subs: {
        new: 0,
        resub: 0,
        gift: 0,
        total: 0,
      },
      gifters: [],
      selfSubs: [],
      bits: { total: 0, top: [] },
      raidsIn: [],
      redemptions: { total: 0, top: [] },
      songRequesters: { total: 0, top: [] },
      topChatters: [{ name: 'kani_dev', count: 12 }],
      commandsRun: { total: 0, top: [] },
    };
    const wrapper = show(quiet);

    await flushPromises();
    const blocks: string[] = wrapper.findAll('.grid [data-block]').map((b) => b.attributes('data-block') ?? '');

    expect(blocks).toEqual(['chatters', 'followers']);
    expect(wrapper.find('.grid').attributes('style')).toContain('grid-template-columns: repeat(2, 1fr)');
    expect(wrapper.find('.grid').attributes('style')).toContain('grid-template-rows: repeat(1, 1fr)');
  });

  it('removes a block with no data', async () => {
    const quiet = {
      ...fixture,
      gifters: [],
      selfSubs: [],
      subs: {
        new: 0,
        resub: 0,
        gift: 0,
        total: 0,
      },
      raidsIn: [],
    };
    const wrapper = show(quiet);

    await flushPromises();
    expect(wrapper.find('[data-block="subs"]').exists()).toBe(false);
    expect(wrapper.find('[data-block="raids"]').exists()).toBe(false);
    expect(wrapper.find('[data-block="chatters"]').exists()).toBe(true);
  });

  it('shows a dash for a missing stat', async () => {
    const { peakViewers: _left, ...noPeak } = fixture;
    const wrapper = show(noPeak);

    await flushPromises();
    expect(wrapper.find('[data-block="peak"] .num').text()).toBe('—');
  });

  it('never asks anyone to subscribe or follow', async () => {
    const wrapper = show(fixture);

    await flushPromises();
    expect(wrapper.text()).not.toMatch(/subscribe now|follow us|follow me|don't forget to/i);
  });

  it('holds for holdSeconds and then fades', async () => {
    const wrapper: VueWrapper = show(fixture, { holdSeconds: 7 });

    await flushPromises();
    expect(wrapper.find('.recap').attributes('data-visible')).toBe('true');
    vi.advanceTimersByTime(6_999);
    await flushPromises();
    expect(wrapper.find('.recap').attributes('data-visible')).toBe('true');
    vi.advanceTimersByTime(2);
    await flushPromises();
    expect(wrapper.find('.recap').attributes('data-visible')).toBe('false');
  });

  it('picks the hue from the Amsterdam start day of the month', async () => {
    const wrapper = show(fixture);

    await flushPromises();
    const expected = (14 * 360) / 31;

    expect(wrapper.find('.recap').attributes('style')).toContain(`--base: ${expected}deg`);
  });

  it('lists requesters in the music block and no song titles', async () => {
    const wrapper = show({
      ...fixture,
      songRequesters: {
        total: 3,
        top: [{ name: 'finwo_', count: 2 }],
        recent: [{ track: 'Secret Song', artist: 'X' }],
      },
    });

    await flushPromises();
    const text: string = wrapper.find('[data-block="songs"]').text();

    expect(text).toContain('finwo_');
    expect(text).not.toContain('Secret Song');
  });

  it('rolls a list only when it is longer than its card', async () => {
    withOverflow(100, 300);
    const rolling = show(fixture);

    await flushPromises();
    expect(rolling.find('[data-block="raids"] .scroll').classes()).toContain('rolling');
    expect(rolling.findAll('[data-block="raids"] .list').length).toBe(2);

    withOverflow(300, 100);
    const still = show(fixture);

    await flushPromises();
    expect(still.find('[data-block="raids"] .scroll').classes()).not.toContain('rolling');
    expect(still.findAll('[data-block="raids"] .list').length).toBe(1);
  });
});
