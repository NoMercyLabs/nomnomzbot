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

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import PollsView from '../PollsView.vue';

const option = (id: string, title: string, votes: number, share: number, isWinner: boolean) => ({
  id,
  title,
  votes,
  channelPoints: 0,
  sharePercent: share,
  isWinner,
});

const server = vi.hoisted(() => ({
  history: [] as unknown[],
  polls: [] as unknown[],
  predictions: [] as unknown[],
}));

const baseHistory = [
  {
    id: 'p1',
    kind: 'poll',
    title: 'Best snack?',
    status: 'archived',
    startedAt: '2026-10-15T18:00:00Z',
    endedAt: '2026-10-15T18:01:00Z',
    totalVotes: 80,
    options: [option('c1', 'Chips', 30, 38, false), option('c2', 'Fruit', 50, 63, true)],
  },
  {
    id: 'q1',
    kind: 'chat-poll',
    title: 'Map?',
    status: 'closed',
    startedAt: '2026-10-15T17:00:00Z',
    endedAt: null,
    totalVotes: 1,
    options: [option('1', 'Forest', 1, 100, true)],
  },
  {
    id: 'pr1',
    kind: 'prediction',
    title: 'Win the boss?',
    status: 'canceled',
    startedAt: '2026-10-14T17:00:00Z',
    endedAt: null,
    totalVotes: 5,
    options: [option('o1', 'Yes', 3, 60, false), option('o2', 'No', 2, 40, false)],
  },
  {
    id: 'z1',
    kind: 'poll',
    title: 'Nobody answered',
    status: 'archived',
    startedAt: '2026-10-13T17:00:00Z',
    endedAt: null,
    totalVotes: 0,
    options: [option('a', 'A', 0, 0, false), option('b', 'B', 0, 0, false)],
  },
];

const runningPoll = {
  id: 'live1',
  broadcasterId: 'b',
  broadcasterName: 'B',
  broadcasterLogin: 'b',
  title: 'Play next?',
  choices: [
    {
      id: 'x',
      title: 'Cats',
      votes: 7,
      channelPointsVotes: 0,
      bitsVotes: 0,
    },
    {
      id: 'y',
      title: 'Dogs',
      votes: 3,
      channelPointsVotes: 0,
      bitsVotes: 0,
    },
  ],
  bitsVotingEnabled: false,
  bitsPerVote: 0,
  channelPointsVotingEnabled: false,
  channelPointsPerVote: 0,
  status: 'ACTIVE',
  duration: 120,
  startedAt: new Date().toISOString(),
  endedAt: null,
};

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      let data = server.predictions;

      if (path.endsWith('/history')) {
        data = server.history;
      } else if (path.endsWith('/polls')) {
        data = server.polls;
      }

      return { data: { data }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  localStorage.clear();
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
  server.history = baseHistory;
  server.polls = [];
  server.predictions = [];
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(PollsView, {
    attachTo: document.body,
    global: {
      plugins: [i18n, [VueQueryPlugin, { queryClient }]],
    },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 200));
  await flushPromises();
}

const cardTexts = (): string[] =>
  Array.from(document.querySelectorAll('[data-history-card]')).map(
    (card) => card.textContent ?? '',
  );

function clickText(selector: string, text: string): void {
  const target = Array.from(document.querySelectorAll(selector)).find(
    (element) => element.textContent?.trim() === text,
  );

  (target as HTMLElement).click();
}

describe('PollsView lead panel', () => {
  it('offers one primary Start a poll with an outline Prediction sibling when nothing runs', async () => {
    await open();

    const lead = document.querySelector('[data-lead="empty"]');

    expect(lead).not.toBeNull();
    expect(document.querySelector('[data-lead="running"]')).toBeNull();

    const start = lead?.querySelector('[data-lead-start="poll"]');
    const prediction = lead?.querySelector('[data-lead-start="prediction"]');

    expect(start?.textContent?.trim()).toBe(en.dashboard.polls.lead.startPoll);
    expect(start?.getAttribute('data-variant')).toBe('default');
    expect(prediction?.getAttribute('data-variant')).toBe('outline');
  });

  it('opens the existing poll dialog from Start a poll', async () => {
    await open();

    (document.querySelector('[data-lead-start="poll"]') as HTMLElement).click();
    await flushPromises();

    expect(document.body.textContent).toContain(en.dashboard.polls.lead.startPoll);
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
  });

  it('shows the running poll with large bars and its End action instead of the start buttons', async () => {
    server.polls = [runningPoll];
    await open();

    const lead = document.querySelector('[data-lead="running"]');

    expect(lead).not.toBeNull();
    expect(lead?.textContent).toContain('Play next?');
    expect(lead?.textContent).toContain('Cats');
    expect(lead?.textContent).toContain('70%');
    expect(lead?.querySelector('[data-lead-end]')).not.toBeNull();
    expect(lead?.querySelector('[data-lead-start]')).toBeNull();

    const leading = lead?.querySelectorAll('[data-bar-fill][data-leading]');

    expect(leading).toHaveLength(1);
    expect(leading?.[0]?.className).toContain('bg-primary');
  });
});

describe('PollsView history', () => {
  it('shows compact cards with the winner and its share, never in the accent', async () => {
    await open();

    const cards = cardTexts();

    expect(cards).toHaveLength(3);
    expect(cards[0]).toContain('Best snack?');
    expect(cards[0]).toContain(en.dashboard.polls.kind.poll);
    expect(cards[0]).toContain('Fruit');
    expect(cards[0]).toContain('63%');

    const cardHtml = Array.from(document.querySelectorAll('[data-history-card]'))
      .map((card) => card.innerHTML)
      .join('');

    expect(cardHtml).not.toContain('bg-primary');
    expect(cardHtml).not.toContain('text-primary');
  });

  it('shows a status only when it carries meaning', async () => {
    await open();

    const cards = cardTexts();

    expect(cards[0]).not.toContain(en.dashboard.polls.status.archived);
    expect(cards[1]).not.toContain(en.dashboard.polls.status.closed);
    expect(cards[2]).toContain(en.dashboard.polls.status.canceled);
  });

  it('collapses polls with no votes into one line that expands', async () => {
    await open();

    expect(cardTexts().some((card) => card.includes('Nobody answered'))).toBe(false);

    const toggle = document.querySelector('[data-zero-votes-toggle]');

    expect(toggle?.textContent).toContain('1');

    (toggle as HTMLElement).click();
    await flushPromises();

    expect(cardTexts().some((card) => card.includes('Nobody answered'))).toBe(true);
  });

  it('filters by kind and keeps the chosen kind after a remount', async () => {
    await open();

    clickText('[data-segment]', en.dashboard.polls.kinds['chat-poll']);
    await flushPromises();

    expect(cardTexts().map((card) => card.includes('Map?'))).toEqual([true]);

    mounted?.unmount();
    document.body.innerHTML = '';
    await open();

    expect(localStorage.getItem('nnz.polls.kind')).toBe('chat-poll');
    expect(cardTexts()).toHaveLength(1);
  });
});
