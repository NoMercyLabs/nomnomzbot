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
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { onTheWire } from '@/api/__tests__/wire';
import { i18n } from '@/i18n';

import AnalyticsView from '../AnalyticsView.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, unknown>; query?: Record<string, unknown> } };
type Call = { path: string; query: Record<string, unknown> | undefined };

const calls: Call[] = [];

const summary = {
  from: '2026-09-15',
  to: '2026-10-14',
  totalMessages: 1200,
  newFollowers: 14,
  newSubscribers: 3,
  bitsCheered: 500,
  commandsRun: 80,
  redemptionsCount: 20,
  songRequests: 6,
  currencyEarnedTotal: 0,
  currencySpentTotal: 0,
  peakViewers: 31,
  deltas: {
    messagesPct: 0,
    followersPct: 0,
    subscribersPct: 0,
    bitsPct: 0,
    commandsPct: 0,
    redemptionsPct: 0,
  },
};

const day = (date: string, followers: number, chatters: number): Record<string, unknown> => ({
  activityDate: date,
  uniqueChatters: chatters,
  totalMessages: chatters * 10,
  totalWatchSeconds: 0,
  newFollowers: followers,
  newSubscribers: 0,
  bitsCheered: 0,
  commandsRun: 0,
  redemptionsCount: 0,
  songRequests: 0,
  currencyEarnedTotal: 0,
  currencySpentTotal: 0,
  gamesPlayed: 0,
  peakViewers: null,
});

const stream = (
  id: string,
  startedAt: string,
  seconds: number,
  game: string,
): Record<string, unknown> => ({
  streamId: id,
  title: `Stream ${id}`,
  gameName: game,
  startedAt,
  endedAt: startedAt,
  durationSeconds: seconds,
  peakViewers: id === 's2'
    ? null
    : 20,
  totalMessages: seconds / 10,
  uniqueChatters: seconds / 300,
  newFollowers: seconds / 1800,
});

// Sunday is 0. Tuesday 16:00 is the busiest hour; Thursday only chats at 16:00.
const chatHours = {
  timeZone: 'Europe/Amsterdam',
  cells: [
    {
      dayOfWeek: 2,
      hour: 16,
      messages: 900,
      chatters: 20,
    },
    {
      dayOfWeek: 2,
      hour: 18,
      messages: 300,
      chatters: 9,
    },
    {
      dayOfWeek: 4,
      hour: 16,
      messages: 600,
      chatters: 14,
    },
  ],
};

// Newest first, as the server sends them; s3 is older than 30 days, so a 30-day page stops there.
const streams = [
  stream('s1', '2026-10-10T18:00:00Z', 9000, 'Software and Game Development'),
  stream('s2', '2026-10-01T18:00:00Z', 5400, 'Just Chatting'),
  stream('s3', '2026-08-01T18:00:00Z', 3600, 'Just Chatting'),
];

function answer(path: string, options: Options | undefined): unknown {
  calls.push({ path, query: options?.params?.query });

  if (path.endsWith('/analytics/channel/summary'))
    return { data: { data: onTheWire(summary) }, response: { ok: true, status: 200 } };

  if (path.endsWith('/analytics/channel/chat-hours'))
    return { data: { data: onTheWire(chatHours) }, response: { ok: true, status: 200 } };

  if (path.endsWith('/analytics/channel/daily')) {
    const days = [day('2026-10-01', 4, 12), day('2026-10-10', 10, 30)];

    return { data: { data: onTheWire(days) }, response: { ok: true, status: 200 } };
  }

  // Page 1 holds every stream; page 2 is the empty last page.
  if (path.endsWith('/analytics/streams')) {
    const first = options?.params?.query?.Page === 1;
    const page = first
      ? {
          data: onTheWire(streams),
          hasMore: true,
          nextPage: 2,
        }
      : {
          data: [],
          hasMore: false,
          nextPage: null,
        };

    return { data: page, response: { ok: true, status: 200 } };
  }

  return { data: { data: null }, response: { ok: true, status: 200 } };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => answer(path, options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1'), current: ref({ login: 'stoney_eagle' }) }),
}));

const access = vi.hoisted(() => ({ held: [] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => access.held.includes(key),
  }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  access.held = ['analytics:read'];
  calls.length = 0;
  localStorage.clear();
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date('2026-10-14T12:00:00'));
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.useRealTimers();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(to = '/app/analytics'): Promise<void> {
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [{
      path: '/app/analytics',
      name: 'dashboard-analytics',
      component: { template: '<div />' },
    }],
  });

  await router.push(to);

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(AnalyticsView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
        [VueQueryPlugin, { queryClient }],
      ],
      // The drawing is ChartPanel.spec's job; jsdom cannot unmount it. The charts' hidden data tables stay.
      stubs: { ChartContainer: true },
    },
  });

  await settle();
}

const tile = (label: string): string =>
  [...document.body.querySelectorAll('[data-slot="stat-tile"]')]
    .find((element) => element.textContent?.includes(label))
    ?.textContent ?? '';
const asked = (suffix: string): Call[] => calls.filter((call) => call.path.endsWith(suffix));

describe('the Analytics overview', () => {
  it('shows the range in numbers: streams, live time, chat, follows, Twitch subs, bits and peak viewers', async () => {
    await open();

    expect(asked('/analytics/channel/summary').map((call) => call.query)).toEqual([
      { from: '2026-09-15', to: '2026-10-14' },
    ]);

    expect(tile('Streams')).toContain('2');
    expect(tile('Streams')).toContain('4 hr live');
    expect(tile('Chat lines')).toContain('1,200');
    expect(tile('Chat lines')).toContain('300 per live hour');
    expect(tile('New follows')).toContain('14');
    // The server counts new subs, resubs and gifts together, so the tile says so.
    expect(tile('Twitch subs')).toContain('3');
    expect(tile('Twitch subs')).toContain('New, renewed and gifted');
    expect(tile('Bits cheered')).toContain('500');
    expect(tile('Peak viewers')).toContain('31');
  });

  it('reads the stream list only as far back as the range', async () => {
    await open();

    expect(asked('/analytics/streams')).toHaveLength(1);
  });

  it('names the average stream and the category with the most live time', async () => {
    await open();

    const highlights = document.body.querySelector('[data-analytics-highlights]')?.textContent ?? '';

    expect(highlights).toContain('Your average stream ran 2 hr.');
    expect(highlights).toContain('Software and Game Development: 3 hr over 1 stream.');
    expect(highlights).toContain('Chat ran 80 commands.');
    // Song requests also come from the !sr command, so they are their own fact, not a part of the rewards.
    expect(highlights).toContain('20 rewards redeemed.');
    expect(highlights).toContain('6 songs requested.');
    expect(highlights).not.toContain('of them');
  });

  it('says how many follows came in and charts them per day', async () => {
    await open();

    const growth = document.body.querySelector('[data-analytics-growth]')?.textContent ?? '';

    expect(growth).toContain('You gained 14 follows in the last 30 days.');
    expect(growth).toContain('Oct 10');
  });

  it('asks for 90 days when the person picks it, and remembers the choice', async () => {
    await open();

    const ninety = [...document.body.querySelectorAll<HTMLButtonElement>('[data-segmented] button')]
      .find((button) => button.textContent?.trim() === '90 days')!;

    ninety.click();
    await settle();

    expect(asked('/analytics/channel/summary').at(-1)?.query).toEqual({ from: '2026-07-17', to: '2026-10-14' });
    expect(localStorage.getItem('nnz.analytics.range')).toBe('r90');
  });
});

const textOf = (selector: string): string[] =>
  [...document.body.querySelectorAll(selector)].map((element) =>
    element.textContent?.replace(/\s+/g, ' ').trim() ?? '');

// A bar item reads as its parts with a space between: label, detail and number.
const barsOf = (selector: string): string[] =>
  [...document.body.querySelectorAll(`${selector} [data-bar-item] > div:first-child`)].map((line) =>
    [...line.children]
      .map((part) =>
        part.textContent?.trim() ?? '')
      .filter((part) =>
        part !== '')
      .join(' '));

describe('the Streams tab', () => {
  it('lists every stream in the range, newest first, with what it brought', async () => {
    await open('/app/analytics?view=streams');

    const panel = document.body.querySelector('[data-analytics-streams]')?.textContent ?? '';

    expect(panel).toContain('2 streams');
    expect(panel).toContain('Your average stream ran 2 hr. The busiest chat was on Sat, Oct 10, with 900 lines.');

    const rows = textOf('[data-stream-row]');

    expect(rows).toHaveLength(2);
    expect(rows[0]).toContain('Sat, Oct 10');
    expect(rows[0]).toContain('2 hr 30 min');
    expect(rows[0]).toContain('30 chatters, 5 new follows');
    expect(rows[1]).toContain('1 hr 30 min');
    // Twitch sent no peak for this stream, so the cell says so instead of showing 0.
    expect(rows[1]).toContain('Not known');
  });
});

describe('the Chat tab', () => {
  it('asks for chat by day and hour over the range and names the busiest hour', async () => {
    await open('/app/analytics?view=chat');

    expect(asked('/analytics/channel/chat-hours').map((call) => call.query)).toEqual([
      { from: '2026-09-15', to: '2026-10-14' },
    ]);

    const heat = document.body.querySelector('[data-analytics-heat]')?.textContent ?? '';

    expect(heat).toContain('Times are in Europe/Amsterdam.');
    expect(heat).toContain('Chat is busiest on Tuesday from 4:00 PM to 5:00 PM.');
    expect(document.body.querySelectorAll('[data-heat-cell]')).toHaveLength(7 * 24);
  });

  it('ranks the weekdays from Monday and the busiest hours by their part of all chat', async () => {
    await open('/app/analytics?view=chat');

    const busy = document.body.querySelector('[data-analytics-busy]')?.textContent ?? '';

    expect(busy).toContain('Tuesday has the most chat: 1,200 lines. 5 days have no chat at all.');

    expect(barsOf('[data-analytics-days]').slice(0, 2)).toEqual([
      'Monday no chat 0',
      'Tuesday busiest at 4:00 PM 1,200',
    ]);

    expect(barsOf('[data-analytics-hours]')).toEqual([
      '4:00 PM to 5:00 PM 83% 1,500',
      '6:00 PM to 7:00 PM 17% 300',
    ]);
  });
});

describe('what the person may see', () => {
  it('shows the tabs and the numbers with the analytics read key', async () => {
    await open();

    expect(document.body.querySelector('[role="tablist"]')).not.toBeNull();
    expect(calls.length).toBeGreaterThan(0);
  });

  it('shows no tabs and reads nothing without the analytics read key', async () => {
    access.held = [];
    await open();

    expect(document.body.querySelector('[role="tablist"]')).toBeNull();
    expect(document.body.querySelector('[role="radiogroup"]')).toBeNull();
    expect(calls).toHaveLength(0);
  });
});
