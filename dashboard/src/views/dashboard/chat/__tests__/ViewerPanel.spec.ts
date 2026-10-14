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

import type { ChatLine } from '@/api/chat';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import ViewerPanel from '../components/ViewerPanel.vue';

type Options = { body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];
let actionOk: boolean = true;

const member = {
  id: 'u1',
  internalUserId: 'g1',
  username: 'dukasoft',
  displayName: 'DukaSoft',
  profileImageUrl: null,
  messageCount: 2811,
  watchHours: 42,
  commandsUsed: 9,
  trustLevel: 'viewer',
  isBanned: false,
  firstSeen: '2026-01-01T00:00:00Z',
  lastSeen: '2026-10-12T18:00:00Z',
  recentActivity: [],
  banHistory: [],
  isVip: null,
  isModerator: null,
};

const profile = {
  identity: {
    userId: 'g1',
    username: 'dukasoft',
    displayName: 'DukaSoft',
  },
  economy: {
    wallet: {
      balance: 6022,
      lifetimeEarned: 6627,
      lifetimeSpent: 605,
    },
  },
};

const currency = {
  currencyName: 'feather',
  currencyNamePlural: 'feathers',
  isEnabled: true,
};

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push({
        method: 'GET',
        path,
        body: undefined,
      });

      const body = path.endsWith('/economy/config')
        ? currency
        : path.endsWith('/profile')
          ? profile
          : member;

      return { data: { data: body }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: options?.body,
      });

      return actionOk
        ? { data: { data: null }, response: { status: 200, ok: true } }
        : { error: {}, response: { status: 500, ok: false } };
    },
  },
}));

const line = (id: string, userId: string, name: string, message: string, at: string): ChatLine =>
  ({
    id,
    channelId: 'c1',
    userId,
    displayName: name,
    username: name.toLowerCase(),
    message,
    fragments: [{ type: 'text', text: message }],
    color: '#ff69b4',
    timestamp: at,
    provider: 'twitch',
  }) as unknown as ChatLine;

const feed: ChatLine[] = [
  line('m1', 'u1', 'DukaSoft', 'first', '2026-10-12T18:01:00Z'),
  line('m2', 'u2', 'Bob', 'not theirs', '2026-10-12T18:02:00Z'),
  line('m3', 'u1', 'DukaSoft', 'second', '2026-10-12T18:03:00Z'),
  line('m4', 'u1', 'DukaSoft', 'third', '2026-10-12T18:04:00Z'),
  line('m5', 'u1', 'DukaSoft', 'fourth', '2026-10-12T18:05:00Z'),
  line('m6', 'u1', 'DukaSoft', 'fifth', '2026-10-12T18:06:00Z'),
  line('m7', 'u1', 'DukaSoft', 'sixth', '2026-10-12T18:07:00Z'),
  line('m8', 'u1', 'DukaSoft', 'seventh', '2026-10-12T18:08:00Z'),
];

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;
  actionOk = true;
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function show(userId: string | null, hour12 = false): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(ViewerPanel, {
    attachTo: document.body,
    props: {
      userId,
      channelId: 'c1',
      lines: feed,
      hour12,
    },
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return mounted;
}

const text = (): string => document.body.textContent ?? '';
const button = (name: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>('button')].find(
    (item) => item.textContent?.trim() === name,
  );

async function press(name: string): Promise<void> {
  button(name)?.click();
  await flushPromises();
}

const posts = (): Call[] =>
  calls.filter((call) => call.method === 'POST');

describe('ViewerPanel', () => {
  it('rounds hours watched to one decimal', async () => {
    member.watchHours = 92.789;
    await show('u1');
    member.watchHours = 42;

    expect(text()).toContain('92.8');
    expect(text()).not.toContain('92.789');
  });

  it('writes times on the same clock as the feed', async () => {
    const times = (): string[] =>
      [...document.body.querySelectorAll('[data-viewer-message] time')].map((item) =>
        item.textContent?.trim() ?? '');

    await show('u1');

    expect(times().every((time) => !/[AP]M/i.test(time))).toBe(true);

    mounted?.unmount();
    await show('u1', true);

    expect(times().every((time) => /[AP]M/i.test(time))).toBe(true);
  });

  it('says what to do when nobody is selected', async () => {
    await show(null);

    expect(text()).toContain(en.dashboard.chat.viewer.empty);
    expect(calls).toHaveLength(0);
    expect(button('Timeout')).toBeUndefined();
    expect(button('Ban')).toBeUndefined();
  });

  it('shows the chatter with a profile hook and the real totals', async () => {
    await show('u1');

    const name = document.body.querySelector('[data-slot="user-name"]');

    expect(name?.getAttribute('data-user')).toBe('u1');
    expect(name?.textContent?.trim()).toBe('DukaSoft');
    expect(text()).toContain('2,811');
    expect(text()).toContain('6,022');
    expect(text()).toContain('6,627');
    expect(text()).toContain('feathers');
  });

  it('lists only their six latest messages here, newest first', async () => {
    await show('u1');

    const shown = [...document.body.querySelectorAll('[data-viewer-message]')].map(
      (item) => item.querySelector('[data-viewer-text]')?.textContent?.trim(),
    );

    expect(shown).toEqual(['seventh', 'sixth', 'fifth', 'fourth', 'third', 'second']);
    expect(text()).not.toContain('not theirs');
  });

  it('explains that other channels are not available instead of listing them', async () => {
    await show('u1');

    expect(text()).toContain(en.dashboard.chat.viewer.otherUnavailable);
    expect(document.body.querySelector('[data-viewer-other-row]')).toBeNull();
  });

  it('times out for the chosen length in this channel and offers undo', async () => {
    await show('u1');
    await press('Timeout');
    await press('10 min');

    const sent = posts().at(-1);

    expect(sent?.path).toBe('/api/v{version}/channels/{channelId}/moderation/actions');

    expect(sent?.body).toMatchObject({
      action: 'timeout',
      targetUserId: 'u1',
      durationSeconds: 600,
    });

    expect(text()).toContain('DukaSoft timed out for 10 min');

    await press('Undo');

    const undone = posts().at(-1);

    expect(undone?.path).toBe('/api/v{version}/channels/{channelId}/moderation/actions/unban');
    expect(undone?.body).toMatchObject({ targetTwitchUserId: 'u1', scope: 'this_channel' });
    expect(button('Timeout')).toBeDefined();
  });

  it('asks before a ban, cancel sends nothing, confirm bans in this channel only', async () => {
    await show('u1');
    await press('Ban');

    expect(text()).toContain('Ban DukaSoft?');

    await press('Cancel');

    expect(posts()).toHaveLength(0);
    expect(button('Timeout')).toBeDefined();

    await press('Ban');

    const confirm = [...document.body.querySelectorAll<HTMLElement>('[data-viewer-ban-confirm]')];

    confirm[0]?.click();
    await flushPromises();

    expect(posts()).toHaveLength(1);
    expect(posts()[0]?.path).toBe('/api/v{version}/channels/{channelId}/moderation/actions/ban');
    expect(posts()[0]?.body).toMatchObject({ targetTwitchUserId: 'u1', scope: 'this_channel' });
    expect(text()).toContain('DukaSoft is banned');
    expect(button('Unban')).toBeDefined();
  });

  it('says a refused action failed and gives the actions back', async () => {
    actionOk = false;
    await show('u1');
    await press('Timeout');
    await press('1 min');

    expect(document.body.querySelector('[role="alert"]')?.textContent).toContain(
      en.dashboard.chat.viewer.failed,
    );

    expect(button('Timeout')).toBeDefined();
  });

  it('asks the page to close when the close button is pressed', async () => {
    const wrapper = await show('u1');

    document.body.querySelector<HTMLElement>('[data-viewer-close]')?.click();
    await flushPromises();

    expect(wrapper.emitted('close')).toHaveLength(1);
  });
});
