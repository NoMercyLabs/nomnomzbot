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

const history = [
  {
    id: 'p1',
    kind: 'poll',
    title: 'Best snack?',
    status: 'completed',
    startedAt: '2026-10-15T18:00:00Z',
    endedAt: '2026-10-15T18:01:00Z',
    totalVotes: 80,
    options: [option('c1', 'Chips', 30, 38, false), option('c2', 'Fruit', 50, 63, true)],
  },
  {
    id: 'q1',
    kind: 'chat-poll',
    title: 'Map?',
    status: 'open',
    startedAt: '2026-10-15T17:00:00Z',
    endedAt: null,
    totalVotes: 1,
    options: [option('1', 'Forest', 1, 100, false)],
  },
];

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({ data: { data: history }, response: { status: 200, ok: true } }),
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

const rowTexts = (): string[] =>
  Array.from(document.querySelectorAll('[data-poll-row]')).map((row) => row.textContent ?? '');

describe('PollsView', () => {
  it('lists every poll with its kind, status and choices, and marks only the winner', async () => {
    await open();

    const rows = rowTexts();

    expect(rows).toHaveLength(2);
    expect(rows[0]).toContain('Best snack?');
    expect(rows[0]).toContain(en.dashboard.polls.kind.poll);
    expect(rows[0]).toContain(en.dashboard.polls.status.completed);
    expect(rows[0]).toContain('50 votes · 63%');
    expect(rows[1]).toContain(en.dashboard.polls.kind['chat-poll']);

    const winners = Array.from(document.querySelectorAll('[data-poll-option][data-winner] [data-option-title]'));

    expect(winners.map((winner) => winner.textContent)).toEqual(['Fruit']);
  });

  it('filters by kind and keeps the chosen kind after a remount', async () => {
    await open();

    const chatPolls = Array.from(document.querySelectorAll('[data-segment]')).find(
      (segment) => segment.textContent?.trim() === en.dashboard.polls.kinds['chat-poll'],
    );

    (chatPolls as HTMLElement).click();
    await flushPromises();

    expect(rowTexts().map((row) => row.includes('Map?'))).toEqual([true]);

    mounted?.unmount();
    document.body.innerHTML = '';
    await open();

    expect(localStorage.getItem('nnz.polls.kind')).toBe('chat-poll');
    expect(rowTexts()).toHaveLength(1);
  });
});
