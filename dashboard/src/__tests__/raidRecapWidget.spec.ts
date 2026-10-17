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
};

const fixture = {
  stream: {
    id: 's1',
    startedAt: '2026-10-15T11:59:00Z',
    endedAt: '2026-10-16T02:00:00Z',
    durationSeconds: 50460,
    peakViewers: 36,
    title: 'Coding and tunes',
    game: 'Software and Game Development',
  },
  followers: 3,
  subs: {
    new: 2,
    resub: 1,
    gift: 4,
    total: 7,
  },
  giftSubs: [
    { name: 'iFiLLz', count: 11 },
    { name: 'finwo_', count: 10 },
  ],
  selfSubscribers: ['kani_dev'],
  bits: { total: 200, top: [{ name: 'iFiLLz', bits: 200 }] },
  raidsIn: [
    { name: 'MayaRainer', viewers: 49 },
    { name: 'theCodeDrift', viewers: 23 },
  ],
  redemptions: { count: 34, top: [{ title: 'Hydrate!', count: 6 }] },
  songs: { count: 206, topRequesters: [{ name: 'finwo_', count: 86 }] },
  chat: {
    messages: 2123,
    uniqueChatters: 46,
    topChatters: [{ name: 'iFiLLz', messages: 436 }],
  },
  commandsRun: 340,
  topCommands: [{ name: '!hug', count: 120 }],
};

const wrappers: VueWrapper[] = [];

function show(data: unknown, settings: Record<string, unknown> = {}): VueWrapper {
  const wrapper = mount(RaidRecap);

  wrappers.push(wrapper);
  settingsHandler(settings);
  handlers['raid_recap.show']!(data);

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

  it('removes a block with no data', async () => {
    const quiet = {
      ...fixture,
      giftSubs: [],
      selfSubscribers: [],
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
    const wrapper = show({ ...fixture, stream: { ...fixture.stream, peakViewers: null } });

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
      songs: {
        count: 3,
        topRequesters: [{ name: 'finwo_', count: 2 }],
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
