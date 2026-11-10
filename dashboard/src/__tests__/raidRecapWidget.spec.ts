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
  newFollowers: ['FirstFollower', 'SecondFollower', 'ThirdFollower'],
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

// Shows the recap with exactly the given settings, so a test sees what a channel with those stored settings sees.
function showWith(data: unknown, settings: Record<string, unknown> = {}): VueWrapper {
  sdk.data.streamStats.mockResolvedValue(data);
  const wrapper = mount(RaidRecap);

  wrappers.push(wrapper);
  settingsHandler(settings);
  handlers['raid_recap.show']!({ stream: { id: 's1' } });

  return wrapper;
}

// The card grid is Stoney's personal look, kept as the `grid` layout: these tests pin it.
function show(data: unknown, settings: Record<string, unknown> = {}): VueWrapper {
  return showWith(data, {
    layout: 'grid',
    hueByDay: true,
    timeZone: 'Europe/Amsterdam',
    ...settings,
  });
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
      ]),
    );

    // Eight data blocks have data: no metric is dropped while a cell is free, and there is no card for the streamer.
    expect(blocks).toContain('topCommands');
    expect(blocks).not.toContain('streamer');

    expect(wrapper.find('[data-block="peak"]').text()).toContain('36');
    expect(wrapper.find('[data-block="subs"]').text()).toContain('iFiLLz');
    expect(wrapper.find('[data-block="subs"]').text()).toContain('kani_dev');
    expect(wrapper.find('[data-block="raids"]').text()).toContain('MayaRainer');
  });

  it('lists who followed under the new followers count', async () => {
    const wrapper = show(fixture);

    await flushPromises();
    const text: string = wrapper.find('[data-block="followers"]').text();

    expect(text).toContain('FirstFollower');
    expect(text.indexOf('FirstFollower')).toBeLessThan(text.indexOf('ThirdFollower'));
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

    expect(blocks).toEqual(['chatters', 'followers']);

    expect(spans(wrapper)).toEqual([
      [6, 1],
      [6, 1],
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
        'redemptions',
        'topCommands',
      ]);
    });

    it('lays seven data blocks as 4 over 3 and gives the streamer no card', async () => {
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
        'followers',
        'redemptions',
        'topCommands',
      ]);

      expect(spans(wrapper)).toEqual([
        [3, 1],
        [3, 1],
        [3, 1],
        [3, 1],
        [4, 1],
        [4, 1],
        [4, 1],
      ]);

      expect(wrapper.find('.grid').attributes('style')).toContain(
        'grid-template-rows: repeat(2, minmax(0, 1fr))',
      );
    });

    it('fills every row to the full width whatever the count: 6 cards are 3 over 3, 5 are 3 over 2, 4 are 2 over 2', async () => {
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

      expect(counts.six).toEqual(Array.from({ length: 6 }, () => [4, 1]));

      expect(counts.five).toEqual([
        [4, 1],
        [4, 1],
        [4, 1],
        [6, 1],
        [6, 1],
      ]);

      expect(counts.four).toEqual(Array.from({ length: 4 }, () => [6, 1]));
    });

    it('lays all eight data blocks as 4 over 4 with no streamer card', async () => {
      const full = { ...fixture, followers: 3 };
      const wrapper = show(full, { labels });

      await flushPromises();
      const blocks: string[] = wrapper
        .findAll('.grid [data-block]')
        .map((b) => b.attributes('data-block') ?? '');

      expect(blocks).toHaveLength(8);
      expect(blocks).not.toContain('streamer');
      expect(blocks).toContain('topCommands');
      expect(spans(wrapper)).toEqual(Array.from({ length: 8 }, () => [3, 1]));

      expect(wrapper.find('.grid').attributes('style')).toContain(
        'grid-template-rows: repeat(2, minmax(0, 1fr))',
      );
    });

    it('draws the channel avatar as one watermark and names the channel in the header', async () => {
      const channel = { name: 'Stoney_Eagle', avatarUrl: 'https://cdn.example/stoney.png' };
      const wrapper = show({ ...streamRecap, channel }, { labels });

      await flushPromises();
      const marks = wrapper.findAll('img.watermark');

      expect(marks).toHaveLength(1);
      expect(marks.at(0)?.attributes('src')).toBe('https://cdn.example/stoney.png');
      expect(marks.at(0)?.attributes('crossorigin')).toBe('anonymous');
      expect(marks.at(0)?.attributes('alt')).toBe('');
      expect(wrapper.findAll('img.avatar')).toHaveLength(0);
      expect(wrapper.find('.sub').text()).toContain('Stoney_Eagle');
      expect(wrapper.find('.sub').text()).not.toContain('NoMercyTV');
    });

    it('draws no watermark when the channel has no avatar', async () => {
      const channel = { name: 'Stoney_Eagle', avatarUrl: null };
      const wrapper = show({ ...streamRecap, channel }, { labels });

      await flushPromises();
      expect(wrapper.findAll('img.watermark')).toHaveLength(0);
      expect(wrapper.findAll('img')).toHaveLength(0);
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
        'Thanks for watching! You spent 9 hours with us.',
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

  describe('the credits layout, the general default (Stoney, 2026-11-02: a scrolling list like many streamers have as their end screen)', () => {
    const channel = { name: 'Quinn_Demo', avatarUrl: 'https://cdn.example/quinn.png' };
    const full = {
      ...fixture,
      channel,
      hypeTrain: { level: 4, trains: 1 },
    };
    const sectionKeys = (wrapper: VueWrapper): string[] =>
      wrapper
        .findAll('.track:first-child [data-section]')
        .map((s) => s.attributes('data-section') ?? '');

    it('is the layout of a stored widget that has no layout key', async () => {
      const wrapper = showWith(full);

      await flushPromises();

      expect(wrapper.find('.recap').attributes('data-layout')).toBe('credits');
      expect(wrapper.find('.grid').exists()).toBe(false);
      expect(wrapper.find('.roll').exists()).toBe(true);
    });

    it('rolls one continuous column of sections, each a heading and a list of names', async () => {
      const wrapper = showWith(full);

      await flushPromises();

      expect(sectionKeys(wrapper)).toEqual([
        'chatters',
        'subs',
        'followers',
        'raids',
        'redemptions',
        'songs',
        'bits',
        'topCommands',
      ]);

      const subs = wrapper.find('.track:first-child [data-section="subs"]');

      expect(subs.find('h2').text()).toBe('Subscriptions');
      expect(subs.text()).toContain('Gifted');
      expect(subs.text()).toContain('Subscribed');
      expect(subs.text().indexOf('iFiLLz')).toBeLessThan(subs.text().indexOf('kani_dev'));
      expect(subs.find('.entry .v').text()).toBe('11');

      const raids = wrapper.find('.track:first-child [data-section="raids"]');

      expect(raids.text()).toContain('MayaRainer');
      expect(raids.text()).toContain('49');
      expect(wrapper.find('.track:first-child .hype').text()).toBe('Hype train reached level 4.');
    });

    it('drops a section that has no data and the hype line without a train', async () => {
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
        bits: { total: 0, top: [] },
        hypeTrain: null,
      };
      const wrapper = showWith(quiet);

      await flushPromises();

      expect(sectionKeys(wrapper)).toEqual([
        'chatters',
        'followers',
        'redemptions',
        'songs',
        'topCommands',
      ]);

      expect(wrapper.find('.hype').exists()).toBe(false);
    });

    it('honors the section switches of the settings', async () => {
      const wrapper = showWith(full, { showSubs: false, showBits: false });

      await flushPromises();

      expect(sectionKeys(wrapper)).not.toContain('subs');
      expect(sectionKeys(wrapper)).not.toContain('bits');
      expect(wrapper.find('.hype').exists()).toBe(false);
    });

    it('keeps a fixed header: avatar, title, channel, date and time live', async () => {
      const wrapper = showWith(full, { timeZone: 'Europe/Amsterdam' });

      await flushPromises();
      const header = wrapper.find('header.top');

      expect(header.find('img.avatar').attributes('src')).toBe('https://cdn.example/quinn.png');
      expect(header.find('img.avatar').attributes('crossorigin')).toBe('anonymous');
      expect(header.find('h1').text()).toBe('Stream recap');
      expect(header.find('.channel').text()).toBe('Quinn_Demo');
      expect(header.find('.date').text()).toBe('Oct 15, 2026');
      expect(header.find('.live').text()).toBe('Time live 14h 01m');
      expect(wrapper.findAll('header.top')).toHaveLength(1);
      expect(wrapper.find('.roll header').exists()).toBe(false);
    });

    it('draws no avatar when the channel has none, and never the NomNomz logo', async () => {
      const wrapper = showWith({ ...full, channel: { name: 'Quinn_Demo', avatarUrl: null } });

      await flushPromises();

      expect(wrapper.findAll('img')).toHaveLength(0);
      expect(wrapper.find('svg.logo').exists()).toBe(false);
    });

    it('rolls the column with the rollSeconds duration and loops it with a second copy', async () => {
      const wrapper = showWith(full, { rollSeconds: 25 });

      await flushPromises();
      const roll = wrapper.find('.roll');

      expect(wrapper.find('.recap').attributes('data-motion')).toBe('roll');
      expect(roll.attributes('style')).toContain('--roll-seconds: 25s');

      const tracks = roll.findAll('.track');

      expect(tracks).toHaveLength(2);
      expect(tracks.at(0)?.attributes('aria-hidden')).toBeUndefined();
      expect(tracks.at(1)?.attributes('aria-hidden')).toBe('true');
      expect(tracks.at(1)?.text()).toBe(tracks.at(0)?.text());
    });

    it('rolls 40 seconds a pass by default and clamps the setting to 10 through 300', async () => {
      const seconds = async (setting: unknown): Promise<string> => {
        const wrapper = showWith(full, setting === undefined
          ? {}
          : { rollSeconds: setting });

        await flushPromises();

        return /--roll-seconds: (\d+)s/.exec(wrapper.find('.roll').attributes('style') ?? '')?.[1] ?? '';
      };

      expect(await seconds(undefined)).toBe('40');
      expect(await seconds(2)).toBe('10');
      expect(await seconds(900)).toBe('300');
      expect(await seconds('not a number')).toBe('40');
    });

    it('still rolls a short list, with a gap before the loop', async () => {
      const short = {
        stream: fixture.stream,
        duration: 600,
        topChatters: [{ name: 'kani_dev', count: 3 }],
      };
      const wrapper = showWith(short);

      await flushPromises();

      expect(sectionKeys(wrapper)).toEqual(['chatters']);
      expect(wrapper.find('.recap').attributes('data-motion')).toBe('roll');
      expect(wrapper.findAll('.roll .track')).toHaveLength(2);
    });

    it('stops the roll and shows a plain list for prefers-reduced-motion', async () => {
      vi.stubGlobal('matchMedia', (media: string) => ({
        matches: true,
        media,
        addEventListener: () => {},
        removeEventListener: () => {},
      }));

      const wrapper = showWith(full);

      await flushPromises();

      expect(wrapper.find('.recap').attributes('data-motion')).toBe('reduced');
      expect(wrapper.find('.roll').attributes('style') ?? '').not.toContain('--roll-seconds');
      expect(wrapper.findAll('.roll .track')).toHaveLength(1);
      expect(sectionKeys(wrapper)).toContain('chatters');
    });

    it('holds still for a still image when roll is off, showing the header and the top of the list', async () => {
      const wrapper = showWith(full, { roll: false });

      await flushPromises();

      expect(wrapper.find('.recap').attributes('data-motion')).toBe('still');
      expect(wrapper.find('.roll').attributes('style') ?? '').not.toContain('--roll-seconds');
      expect(wrapper.findAll('.roll .track')).toHaveLength(1);
      expect(sectionKeys(wrapper)[0]).toBe('chatters');
    });

    it('closes each pass with the general thank-you line', async () => {
      const wrapper = showWith(full);

      await flushPromises();

      expect(wrapper.find('.track:first-child .thanks').text()).toBe(
        'Thanks for watching! You spent 14 hours with us.',
      );

      expect(wrapper.text()).not.toContain('coding, tunes and vibes');
    });

    it('lets a channel keep its own thank-you line through the thanks label', async () => {
      const wrapper = showWith(full, {
        labels: { thanks: 'Thank you for {duration} of coding, tunes and vibes.' },
      });

      await flushPromises();

      expect(wrapper.find('.track:first-child .thanks').text()).toBe(
        'Thank you for 14 hours of coding, tunes and vibes.',
      );
    });

    it('never asks anyone to subscribe or follow', async () => {
      const wrapper = showWith(full);

      await flushPromises();

      expect(wrapper.text()).not.toMatch(/subscribe now|follow us|follow me|don't forget to/i);
    });

    it('switches to the card grid with layout grid, with every old card from the same recap', async () => {
      const wrapper = showWith(full, { layout: 'grid' });

      await flushPromises();
      const blocks: string[] = wrapper
        .findAll('.grid [data-block]')
        .map((b) => b.attributes('data-block') ?? '');

      expect(wrapper.find('.recap').attributes('data-layout')).toBe('grid');
      expect(wrapper.find('.roll').exists()).toBe(false);
      expect(wrapper.find('svg.logo').exists()).toBe(true);

      expect(blocks).toEqual([
        'subs',
        'raids',
        'chatters',
        'songs',
        'bits',
        'followers',
        'redemptions',
        'topCommands',
      ]);

      expect(wrapper.findAll('img.watermark')).toHaveLength(1);

      expect(wrapper.find('header .thanks').text()).toBe(
        'Thanks for watching! You spent 14 hours with us.',
      );
    });

    it('keeps the default layout when the layout setting is not one of the two', async () => {
      const wrapper = showWith(full, { layout: 'carousel' });

      await flushPromises();

      expect(wrapper.find('.recap').attributes('data-layout')).toBe('credits');
    });
  });

  describe('look settings of the same widget (hueByDay, accentColor, timeZone)', () => {
    const base = (wrapper: VueWrapper): string =>
      /--base: ([^;]+)/.exec(wrapper.find('.recap').attributes('style') ?? '')?.[1] ?? '';
    const day = (iso: string) => ({ ...fixture, stream: { ...fixture.stream, startedAt: iso } });

    it('uses one neutral hue every day when hueByDay is off, and the day rainbow when it is on', async () => {
      const first = showWith(day('2026-10-01T10:00:00Z'));
      const fifteenth = showWith(day('2026-10-15T10:00:00Z'));

      await flushPromises();
      expect(base(first)).toBe(base(fifteenth));

      const rainbowFirst = showWith(day('2026-10-01T10:00:00Z'), { hueByDay: true });
      const rainbowFifteenth = showWith(day('2026-10-15T10:00:00Z'), { hueByDay: true });

      await flushPromises();
      expect(base(rainbowFirst)).not.toBe(base(rainbowFifteenth));
      expect(base(rainbowFirst)).not.toBe(base(first));
    });

    it('colors the accent from accentColor and leaves the neutral accent when it is empty', async () => {
      const own = showWith(fixture, { accentColor: '#22aa66' });
      const neutral = showWith(fixture);

      await flushPromises();

      expect(own.find('.recap').attributes('style')).toContain('--nnz-accent: #22aa66');
      expect(neutral.find('.recap').attributes('style')).not.toContain('--nnz-accent');
    });

    it('reads the date in the time zone of the setting', async () => {
      const lateEvening = day('2026-10-15T23:30:00Z');
      const tokyo = showWith(lateEvening, { timeZone: 'Asia/Tokyo' });
      const losAngeles = showWith(lateEvening, { timeZone: 'America/Los_Angeles' });

      await flushPromises();

      expect(tokyo.find('.date').text()).toBe('Oct 16, 2026');
      expect(losAngeles.find('.date').text()).toBe('Oct 15, 2026');
    });

    it('reads the date in the browser time zone when the setting is empty or not a zone', async () => {
      const lateEvening = day('2026-10-15T23:30:00Z');
      const here = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' }).format(
        new Date(lateEvening.stream.startedAt),
      );
      const empty = showWith(lateEvening, { timeZone: '' });
      const broken = showWith(lateEvening, { timeZone: 'Mars/Olympus' });

      await flushPromises();

      expect(empty.find('.date').text()).toBe(here);
      expect(broken.find('.date').text()).toBe(here);
    });

    it('moves the roll up by one column per pass, and only while the motion is roll', () => {
      const source = readFileSync(
        resolve(
          __dirname,
          '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/raid_recap.vue',
        ),
        'utf8',
      );
      const rolling = /\.recap\[data-motion='roll'\] \.roll\s*\{([^}]*)\}/.exec(source)?.[1] ?? '';
      const keyframes = /@keyframes roll\s*\{(.*)\}\s*\n/.exec(source)?.[1] ?? '';

      expect(rolling).toContain('animation: roll var(--roll-seconds) linear infinite');
      expect(keyframes).toContain('translateY(-50%)');
      expect(source.match(/animation:/g)).toHaveLength(1);
    });

    it('no longer fixes the recap to Europe/Amsterdam', () => {
      const source = readFileSync(
        resolve(
          __dirname,
          '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/raid_recap.vue',
        ),
        'utf8',
      );

      expect(source).not.toContain('Europe/Amsterdam');
    });
  });

  describe('the avatar watermark (Stoney, 2026-10-21: the avatar is a watermark in the bottom-left corner of the bottom-left card, no dedicated image card)', () => {
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

    it.each([1, 2, 3, 4, 5, 6, 7, 8])(
      'sits only inside the first card of the last row when %i metric blocks are shown',
      async (metricBlocks) => {
        const settings = Object.fromEntries(
          SHOWN.map((flag, i) => [flag, i < metricBlocks]),
        );
        const wrapper = show(withChannel, settings);

        await flushPromises();
        const rows = rowsOf(wrapper);
        const last = rows[rows.length - 1]!;
        const marks = wrapper.findAll('img.watermark');
        const host = marks.at(0)?.element.closest('[data-block]');

        expect(wrapper.findAll('.grid [data-block]')).toHaveLength(metricBlocks);
        expect(wrapper.find('[data-block="streamer"]').exists()).toBe(false);
        expect(wrapper.findAll('img')).toHaveLength(1);
        expect(marks).toHaveLength(1);
        expect(host?.getAttribute('data-block')).toBe(last[0]);
        expect(host?.parentElement?.classList.contains('grid')).toBe(true);
      },
    );

    it('draws nothing when no metric block is shown', async () => {
      const settings = Object.fromEntries(SHOWN.map((flag) => [flag, false]));
      const wrapper = show(withChannel, settings);

      await flushPromises();
      expect(wrapper.findAll('.grid [data-block]')).toHaveLength(0);
      expect(wrapper.findAll('img')).toHaveLength(0);
    });

    it('styles the watermark gray, faint and anchored to the bottom left of its card', () => {
      const source = readFileSync(
        resolve(
          __dirname,
          '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/raid_recap.vue',
        ),
        'utf8',
      );
      const rule = /\.watermark\s*\{([^}]*)\}/.exec(source)?.[1] ?? '';
      const opacity = Number(/opacity:\s*([\d.]+)/.exec(rule)?.[1] ?? '1');

      expect(rule).toContain('position: absolute');
      expect(rule).toContain('filter: grayscale(1)');
      expect(rule).toMatch(/left:\s*-?\d/);
      expect(rule).toMatch(/bottom:\s*-?\d/);
      expect(opacity).toBeGreaterThan(0);
      expect(opacity).toBeLessThanOrEqual(0.2);
    });
  });
});
