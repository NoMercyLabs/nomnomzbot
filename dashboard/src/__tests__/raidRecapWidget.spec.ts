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
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import streamRecap from './fixtures/streamRecap20261018.json';
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

const LABEL_PAIR = /^\s*(?:\["([^"]+)"\]|'([^']+)'|(\w+))\s*[=:]\s*["']([^"']*)["']/;

// The key/value lines of a label list in a source file: from `start` (searched after `after`) up to `end`.
function readLabels(path: string, start: string, end: string, after = ''): Record<string, string> {
  const text = readFileSync(resolve(__dirname, path), 'utf8');
  const search = Math.max(0, text.indexOf(after));
  const from = text.indexOf(start, search);
  const stop = text.indexOf(end, from);
  const block = text.slice(from, stop);
  const lines = block.split('\n');
  const matches = lines.map((line) => LABEL_PAIR.exec(line));
  const pairs = matches.filter((m): m is RegExpExecArray => m !== null);

  return Object.fromEntries(pairs.map((m) => [m[1] ?? m[2] ?? m[3], m[4]]));
}

// Each placed block as [columns spanned, rows spanned], read from its inline grid style.
function spans(wrapper: VueWrapper): number[][] {
  return wrapper.findAll('.grid [data-block]').map((block) => {
    const style = block.attributes('style') ?? '';
    const columns = /grid-column: span (\d+)/.exec(style)?.[1] ?? '0';
    const rows = /grid-row: span (\d+)/.exec(style)?.[1] ?? '1';

    return [Number(columns), Number(rows)];
  });
}

// jsdom has no layout: a list box ends at boxBottom, and the nth child of a list ends at (n + 1) * rowHeight.
function withLayout(boxBottom: number, rowHeight: number): void {
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
    this: HTMLElement,
  ) {
    const parent = this.parentElement;
    const index = parent
      ? Array.from(parent.children).indexOf(this)
      : 0;
    const bottom = this.classList.contains('scroll')
      ? boxBottom
      : (index + 1) * rowHeight;

    return { bottom } as DOMRect;
  });
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal('NomNomz', sdk);
});

afterEach(() => {
  wrappers.splice(0).forEach((w) => w.unmount());
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('raid_recap widget', () => {
  it('renders every block from a full recap', async () => {
    const wrapper = show(fixture);

    await flushPromises();
    const blocks: string[] = wrapper
      .findAll('[data-block]')
      .map((b) => b.attributes('data-block') ?? '');

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
        'streamer',
      ]),
    );

    // Eight data blocks have data and the streamer card is the ninth cell: no metric is dropped while a cell is free.
    expect(blocks).toContain('topCommands');

    expect(wrapper.find('[data-block="peak"]').text()).toContain('36');
    expect(wrapper.find('[data-block="subs"]').text()).toContain('iFiLLz');
    expect(wrapper.find('[data-block="subs"]').text()).toContain('kani_dev');
    expect(wrapper.find('[data-block="raids"]').text()).toContain('MayaRainer');
  });

  it('reads the shown stream through data.streamStats with the metrics setting', async () => {
    show(fixture, { metrics: ['followers', 'raidsIn'] });

    await flushPromises();

    expect(sdk.data.streamStats).toHaveBeenLastCalledWith({
      stream: 's1',
      metrics: ['followers', 'raidsIn', 'channel', 'hypeTrain'],
    });
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
    const blocks: string[] = wrapper
      .findAll('.grid [data-block]')
      .map((b) => b.attributes('data-block') ?? '');

    expect(blocks).toEqual(['streamer', 'chatters', 'followers']);

    expect(spans(wrapper)).toEqual([
      [4, 1],
      [4, 1],
      [4, 1],
    ]);

    expect(wrapper.find('.grid').attributes('style')).toContain(
      'grid-template-rows: repeat(1, minmax(0, 1fr))',
    );
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

  it('shows whole rows only: rows past the card edge are cut, never half drawn', async () => {
    const rowHeight = 30;

    withLayout(100, rowHeight);
    const wrapper = show(streamRecap);

    await flushPromises();
    const box = wrapper.find('[data-block="songs"] .scroll');
    const drawn = box.findAll('.list > *').length;

    expect(box.classes()).not.toContain('rolling');
    expect(wrapper.findAll('[data-block="songs"] .list').length).toBe(1);
    expect(drawn).toBe(3);
    expect(drawn * rowHeight).toBeLessThanOrEqual(100);
  });

  it('keeps every row when the list fits the card', async () => {
    withLayout(10000, 30);
    const wrapper = show(streamRecap);

    await flushPromises();
    const rows = wrapper.findAll('[data-block="songs"] .row').length;

    expect(rows).toBe(streamRecap.songRequesters.top.length);
  });

  describe('the 2026-10-18 stream, as the API returned it', () => {
    const labels = {
      'raids.unit.one': 'raid',
      'raids.unit.other': 'raids',
      'viewers.unit.one': 'viewer',
      'viewers.unit.other': 'viewers',
      'songs.unit.one': 'request',
      'songs.unit.other': 'requests',
      'chatters.unit.one': 'chatter',
      'chatters.unit.other': 'chatters',
      'redemptions.unit': 'redeemed',
      thanks: 'Thank you for {duration} of coding, tunes and vibes.',
      'hours.unit.one': 'hour',
      'hours.unit.other': 'hours',
      hypeTrain: 'Hype train reached level {level}.',
    };

    it('renders all 5 rows of every ranked list', async () => {
      const wrapper = show(streamRecap, { labels });

      await flushPromises();
      const counts: Record<string, number> = {};

      ['songs', 'redemptions', 'topCommands'].forEach((key) => {
        counts[key] = wrapper.findAll(`[data-block="${key}"] .row`).length;
      });

      expect(counts).toEqual({
        songs: 5,
        redemptions: 5,
        topCommands: 5,
      });

      expect(wrapper.find('[data-block="songs"]').text()).toContain('iFiLLz');
      expect(wrapper.find('[data-block="redemptions"]').text()).toContain('First');
    });

    it('reads one raid of 11 viewers as "1 raid · 11 viewers"', async () => {
      const wrapper = show(streamRecap, { labels });

      await flushPromises();
      expect(wrapper.find('[data-block="raids"] .big').text()).toBe('1raid · 11 viewers');
    });

    it('uses the plural forms for more than one raid and for one viewer', async () => {
      const wrapper = show(
        {
          ...streamRecap,
          raidsIn: [
            { name: 'a', viewers: 1 },
            { name: 'b', viewers: 0 },
          ],
        },
        { labels },
      );

      await flushPromises();
      expect(wrapper.find('[data-block="raids"] .big').text()).toBe('2raids · 1 viewer');
    });

    it('singularizes the song and chatter units for a count of one', async () => {
      const wrapper = show(
        {
          ...streamRecap,
          songRequesters: { total: 1, top: [{ name: 'finwo_', count: 1 }] },
          chat: { messages: 4, uniqueChatters: 1 },
          topChatters: [{ name: 'finwo_', count: 4 }],
        },
        { labels },
      );

      await flushPromises();
      expect(wrapper.find('[data-block="songs"] .big').text()).toBe('1request');
      expect(wrapper.find('[data-block="chatters"] .big').text()).toBe('1chatter');
    });

    it('hides top chatters when there is no chat data and keeps every card that has data', async () => {
      const wrapper = show(
        {
          ...streamRecap,
          chat: null,
          topChatters: [],
        },
        { labels },
      );

      await flushPromises();
      const blocks: string[] = wrapper
        .findAll('.grid [data-block]')
        .map((b) => b.attributes('data-block') ?? '');

      expect(blocks).toEqual([
        'raids',
        'songs',
        'bits',
        'followers',
        'streamer',
        'redemptions',
        'topCommands',
      ]);
    });

    it('lays seven data blocks and the streamer card as 4 over 4, the streamer card first in the bottom row', async () => {
      const wrapper = show(streamRecap, { labels });

      await flushPromises();
      const blocks: string[] = wrapper
        .findAll('.grid [data-block]')
        .map((b) => b.attributes('data-block') ?? '');

      expect(blocks).toEqual([
        'raids',
        'chatters',
        'songs',
        'bits',
        'streamer',
        'followers',
        'redemptions',
        'topCommands',
      ]);

      expect(spans(wrapper)).toEqual(Array.from({ length: 8 }, () => [3, 1]));

      expect(wrapper.find('.grid').attributes('style')).toContain(
        'grid-template-rows: repeat(2, minmax(0, 1fr))',
      );
    });

    it('fills every row to the full width whatever the count: 7 cells are 4 over 3, 6 are 3 over 3, 5 are 3 over 2', async () => {
      const six = { ...streamRecap, redemptions: { total: 0, top: [] } };
      const five = { ...six, followers: 0 };
      const four = { ...five, bits: { total: 0, top: [] } };
      const counts: Record<string, number[][]> = {};

      for (const [name, data] of Object.entries({
        six,
        five,
        four,
      })) {
        const wrapper = show(data, { labels });

        await flushPromises();
        counts[name] = spans(wrapper);
      }

      expect(counts.six).toEqual([
        [3, 1],
        [3, 1],
        [3, 1],
        [3, 1],
        [4, 1],
        [4, 1],
        [4, 1],
      ]);

      expect(counts.five).toEqual([
        [4, 1],
        [4, 1],
        [4, 1],
        [4, 1],
        [4, 1],
        [4, 1],
      ]);

      expect(counts.four).toEqual([
        [4, 1],
        [4, 1],
        [4, 1],
        [6, 1],
        [6, 1],
      ]);
    });

    it('lays all eight data blocks and the streamer card as 3 rows of 3, the streamer card first in the bottom row', async () => {
      const full = { ...fixture, followers: 3 };
      const wrapper = show(full, { labels });

      await flushPromises();
      const blocks: string[] = wrapper
        .findAll('.grid [data-block]')
        .map((b) => b.attributes('data-block') ?? '');

      expect(blocks).toHaveLength(9);
      expect(blocks.indexOf('streamer')).toBe(6);
      expect(blocks).toContain('topCommands');
      expect(spans(wrapper)).toEqual(Array.from({ length: 9 }, () => [4, 1]));

      expect(wrapper.find('.grid').attributes('style')).toContain(
        'grid-template-rows: repeat(3, minmax(0, 1fr))',
      );
    });

    it('draws the channel avatar in the streamer card and names the channel in the header', async () => {
      const channel = { name: 'Stoney_Eagle', avatarUrl: 'https://cdn.example/stoney.png' };
      const wrapper = show({ ...streamRecap, channel }, { labels });

      await flushPromises();
      const avatars = wrapper.findAll('img.avatar');

      expect(avatars).toHaveLength(1);
      expect(avatars.at(0)?.attributes('src')).toBe('https://cdn.example/stoney.png');
      expect(avatars.at(0)?.attributes('crossorigin')).toBe('anonymous');
      expect(wrapper.find('.grid [data-block="streamer"] img.avatar').exists()).toBe(true);
      expect(wrapper.find('.sub').text()).toContain('Stoney_Eagle');
      expect(wrapper.find('.sub').text()).not.toContain('NoMercyTV');
    });

    it('draws the streamer card without an image when the channel has no avatar', async () => {
      const channel = { name: 'Stoney_Eagle', avatarUrl: null };
      const wrapper = show({ ...streamRecap, channel }, { labels });

      await flushPromises();
      expect(wrapper.findAll('img.avatar')).toHaveLength(0);
      expect(wrapper.find('[data-block="streamer"]').text()).toContain('Stoney_Eagle');
    });

    it('closes the header with a thank-you line that names the stream length in hours', async () => {
      const wrapper = show(streamRecap, { labels });

      await flushPromises();

      expect(wrapper.find('header .thanks').text()).toBe(
        'Thank you for 9 hours of coding, tunes and vibes.',
      );
    });

    it('falls back to the catalog words when the installed settings have no labels', async () => {
      const wrapper = show(streamRecap, { labels: {} });

      await flushPromises();

      expect(wrapper.find('header .thanks').text()).toBe(
        'Thank you for 9 hours of coding, tunes and vibes.',
      );

      expect(wrapper.text()).toContain('chatters');
      expect(wrapper.find('[data-block="raids"] .big').text()).toBe('1raid · 11 viewers');
      expect(wrapper.text()).toContain('requests');
      expect(wrapper.text()).toContain('Top chatters');
    });

    it('groups numbers the en-US way whatever the browser locale is', async () => {
      vi.spyOn(Number.prototype, 'toLocaleString').mockImplementation(function (
        this: number,
        locale?: Intl.LocalesArgument,
      ) {
        return new Intl.NumberFormat(locale ?? 'nl-NL').format(this);
      });

      const wrapper = show(streamRecap, { labels });

      await flushPromises();
      expect(wrapper.text()).toContain('1,107');
      expect(wrapper.text()).not.toContain('1.107');
    });

    it('keeps DEFAULT_LABELS equal to the labels of the catalog', () => {
      const root = '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets';
      const widget = readLabels(`${root}/Assets/raid_recap.vue`, 'const DEFAULT_LABELS', '};');
      const catalog = readLabels(
        // cspell:disable-next-line -- the server file keeps its British name
        `${root}/FirstPartyWidgetCatalogue.cs`,
        '["labels"] = new Dictionary<string, object>',
        '},',
        'Key: "raid_recap"',
      );

      expect(Object.keys(catalog).length).toBeGreaterThan(20);
      expect(widget).toEqual(catalog);
    });

    it('names one hour in the singular', async () => {
      const wrapper = show({ ...streamRecap, duration: 3600 }, { labels });

      await flushPromises();

      expect(wrapper.find('header .thanks').text()).toBe(
        'Thank you for 1 hour of coding, tunes and vibes.',
      );
    });

    it('shows the hype train level in the subscriptions card and hides the line without a train', async () => {
      const subs = { ...streamRecap.subs, total: 3 };
      const withTrain = show(
        {
          ...streamRecap,
          subs,
          hypeTrain: { level: 5, trains: 2 },
        },
        { labels },
      );

      await flushPromises();

      expect(withTrain.find('[data-block="subs"] .hype').text()).toBe(
        'Hype train reached level 5.',
      );

      const without = show(
        {
          ...streamRecap,
          subs,
          hypeTrain: null,
        },
        { labels },
      );

      await flushPromises();
      expect(without.find('.hype').exists()).toBe(false);
      expect(without.text()).not.toContain('Hype train');
    });

    it('keeps the chatters card when only the unique count is known', async () => {
      const chat = { messages: 9, uniqueChatters: 3 };
      const wrapper = show({ ...streamRecap, chat }, { labels });

      await flushPromises();
      expect(wrapper.find('[data-block="chatters"] .big').text()).toBe('3chatters');
    });
  });

  describe('the streamer card (Stoney, 2026-10-20: "i want my avatar to show up always in the left bottom card")', () => {
    const SHOWN = [
      'showSubs',
      'showRaids',
      'showChatters',
      'showSongs',
      'showBits',
      'showFollowers',
      'showRedemptions',
      'showTopCommands',
    ] as const;
    const withChannel = {
      ...fixture,
      channel: { name: 'Stoney_Eagle', avatarUrl: 'https://cdn.example/stoney.png' },
    };

    // The grid cells in reading order, grouped into rows by their column spans (12 columns per row).
    function rowsOf(wrapper: VueWrapper): string[][] {
      const rows: string[][] = [[]];
      let used = 0;

      spans(wrapper).forEach(([columns], i) => {
        const key = wrapper.findAll('.grid [data-block]')[i]!.attributes('data-block') ?? '';

        if (used >= 12) {
          rows.push([]);
          used = 0;
        }

        rows[rows.length - 1]!.push(key);
        used += columns!;
      });

      return rows;
    }

    it.each([0, 1, 2, 3, 4, 5, 6, 7, 8])(
      'is the first cell of the last row when %i metric blocks are shown',
      async (metricBlocks) => {
        const settings = Object.fromEntries(
          SHOWN.map((flag, i) => [flag, i < metricBlocks]),
        );
        const wrapper = show(withChannel, settings);

        await flushPromises();
        const rows = rowsOf(wrapper);
        const last = rows[rows.length - 1]!;

        expect(last[0]).toBe('streamer');
        expect(wrapper.findAll('.grid [data-block]')).toHaveLength(metricBlocks + 1);
        expect(wrapper.findAll('[data-block="streamer"]')).toHaveLength(1);
      },
    );

    it('shows the channel avatar large with the channel name', async () => {
      const wrapper = show(withChannel);

      await flushPromises();
      const card = wrapper.find('[data-block="streamer"]');

      expect(card.find('img.avatar').attributes('src')).toBe('https://cdn.example/stoney.png');
      expect(card.text()).toContain('Stoney_Eagle');
    });
  });
});
