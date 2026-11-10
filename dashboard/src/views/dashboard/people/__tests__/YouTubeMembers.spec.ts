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

// Stoney, 2026-11-10: "we need analytics and all those features we have from twitch to match". The People page
// lists the channel's YouTube members the way it lists Twitch subscribers.
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import YouTubeMembers from '../components/YouTubeMembers.vue';
import en from '../i18n/en.json';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const asked: Record<string, unknown>[] = [];
let answer: (pageToken: string | undefined) => Answer;

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });
const members = (over: Record<string, unknown> = {}) => ({
  access: 'allowed',
  members: [
    {
      channelId: 'UCmember1',
      displayName: 'Marci',
      profileImageUrl: null,
      levelId: 'level-gold',
      levelName: 'Gold',
      memberSince: '2026-03-01T10:00:00Z',
      totalMonths: 8,
    },
    {
      channelId: null,
      displayName: 'A deleted channel',
      profileImageUrl: null,
      levelId: 'level-fan',
      levelName: 'Fan',
      memberSince: '2026-10-20T08:30:00Z',
      totalMonths: 0,
    },
  ],
  levels: [],
  total: 2,
  nextPageToken: null,
  ...over,
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (
      _path: string,
      options?: { params?: { query?: Record<string, unknown> } },
    ): Promise<Answer> => {
      asked.push(options?.params?.query ?? {});

      return answer(options?.params?.query?.pageToken as string | undefined);
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  asked.length = 0;
  answer = () => ok(members());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  const blank = { template: '<div />' };
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: blank },
      {
        path: '/settings/integrations',
        name: 'dashboard-settings-integrations',
        component: blank,
      },
    ],
  });

  await router.push('/');

  mounted = mount(YouTubeMembers, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        router,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await flushPromises();
}

const text = (): string => document.body.textContent ?? '';
const rows = (): string[] =>
  [...document.body.querySelectorAll('[data-youtube-member]')].map((row) =>
    (row.textContent ?? '').replace(/\s+/g, ' ').trim());
const link = (): HTMLAnchorElement | null =>
  document.body.querySelector<HTMLAnchorElement>('[data-youtube-blocked] a');

describe('YouTubeMembers', () => {
  it('lists each member with the level, the day they joined and the months, and counts them', async () => {
    await open();

    expect(rows()).toHaveLength(2);
    expect(rows()[0]).toContain('Marci');
    expect(rows()[0]).toContain('Gold');
    expect(rows()[0]).toContain('Mar 1, 2026');
    expect(document.body.querySelector('[data-youtube-member] td:last-child')?.textContent?.trim()).toBe('8');
    expect(rows()[1]).toContain('A deleted channel');
    expect(rows()[1]).toContain('Fan');
    expect(text()).toContain('2 members');
    expect(document.body.querySelector('[data-youtube-blocked]')).toBeNull();
  });

  it('shows the next page under the first when more is asked for, with the token YouTube gave', async () => {
    answer = (pageToken) =>
      pageToken === 'page-2'
        ? ok(
            members({
              members: [{
                ...members().members[0],
                channelId: 'UCmember3',
                displayName: 'Noor',
              }],
              total: 3,
            }),
          )
        : ok(members({ total: 3, nextPageToken: 'page-2' }));

    await open();

    const more = document.body.querySelector<HTMLButtonElement>('[data-slot="show-more"]');

    expect(more?.textContent).toContain('1 left');

    more?.click();
    await flushPromises();

    expect(asked.at(-1)).toMatchObject({ pageToken: 'page-2' });
    expect(rows()).toHaveLength(3);
    expect(rows()[2]).toContain('Noor');
    expect(document.body.querySelector('[data-slot="show-more"]')).toBeNull();
  });

  it('says the bot may not read the members yet and links to the connected services', async () => {
    answer = () => ok(members({
      access: 'not_allowed',
      members: [],
      total: 0,
    }));

    await open();

    expect(text()).toContain('allow "See your channel members"');
    expect(link()?.getAttribute('href')).toBe('/settings/integrations');
    expect(rows()).toHaveLength(0);
  });

  it('says the channel is not connected to YouTube and links to the connected services', async () => {
    answer = () => ok(members({
      access: 'not_connected',
      members: [],
      total: 0,
    }));

    await open();

    expect(text()).toContain('not connected to YouTube');
    expect(link()?.getAttribute('href')).toBe('/settings/integrations');
  });

  it('says the YouTube channel has no memberships, with nothing to press', async () => {
    answer = () => ok(members({
      access: 'not_enabled',
      members: [],
      total: 0,
    }));

    await open();

    expect(text()).toContain('has no channel memberships');
    expect(link()).toBeNull();
  });

  it('says YouTube lists no members when the list is allowed and empty', async () => {
    answer = () => ok(members({ members: [], total: 0 }));

    await open();

    expect(text()).toContain('YouTube lists no members for your channel.');
  });

  it('shows an error with a retry when the bot refuses, not an empty list', async () => {
    answer = () => ({ error: { status: 'error' }, response: { status: 403, ok: false } });

    await open();

    expect(text()).toContain('The YouTube members could not load.');
    expect(text()).not.toContain('YouTube lists no members');
  });
});
