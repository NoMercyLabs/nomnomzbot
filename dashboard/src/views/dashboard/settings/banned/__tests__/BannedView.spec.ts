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
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed } from 'vue';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import BannedView from '../BannedView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};
type Call = {
  method: string;
  path: string;
  userId: string;
};

const calls = vi.hoisted(() => [] as Call[]);
const access = vi.hoisted(() => ({ unban: true, fails: false }));
const store = vi.hoisted(() => ({ bans: [] as Record<string, unknown>[] }));

const ban = (userId: string, username: string, reason: string | null): Record<string, unknown> => ({
  userId,
  username,
  reason,
  bannedBy: 'Stoney_Eagle',
  bannedAt: '2026-10-01T10:00:00Z',
});

vi.mock('@/api/client', () => ({
  client: {
    use: (): undefined => undefined,
    GET: async (): Promise<Answer> => ({
      data: { data: store.bans },
      response: { ok: true, status: 200 },
    }),
    DELETE: async (
      path: string,
      init: { params: { path: { userId: string } } },
    ): Promise<Answer> => {
      calls.push({
        method: 'DELETE',
        path,
        userId: init.params.path.userId,
      });

      if (access.fails)
        return { error: { detail: 'Twitch said no' }, response: { ok: false, status: 502 } };

      store.bans = store.bans.filter((row) => row.userId !== init.params.path.userId);

      return { response: { ok: true, status: 200 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'moderation:unban' || access.unban }),
}));

vi.mock('vue-sonner', () => ({
  toast: { success: vi.fn<() => void>(), error: vi.fn<() => void>() },
}));

let wrapper: VueWrapper | undefined;

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(BannedView, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  access.unban = true;
  access.fails = false;
  calls.length = 0;
  store.bans = [ban('u1', 'spammer1', 'Spam links'), ban('u2', 'troll2', null)];
});

afterEach(() => {
  wrapper?.unmount();
  document.body.innerHTML = '';
  vi.clearAllMocks();
});

describe('BannedView', () => {
  it('lists each banned person with the reason and who banned them', async () => {
    const page = await open();

    expect(page.findAll('[data-ban]').map((row) => row.attributes('data-ban'))).toEqual(['u1', 'u2']);
    expect(page.get('[data-ban="u1"]').text()).toContain('Spam links');
    expect(page.get('[data-ban="u1"]').text()).toContain('Banned by Stoney_Eagle');
    expect(page.get('[data-ban="u2"]').text()).not.toContain('Spam links');
  });

  it('unbans one person by id and shows the shorter list', async () => {
    const page = await open();

    await page.get('[data-ban-lift="u1"]').trigger('click');
    await flushPromises();

    expect(calls).toEqual([
      {
        method: 'DELETE',
        path: '/api/v{version}/channels/{channelId}/moderation/bans/{userId}',
        userId: 'u1',
      },
    ]);

    expect(page.findAll('[data-ban]').map((row) => row.attributes('data-ban'))).toEqual(['u2']);
  });

  it('keeps the person on the list when Twitch refuses the unban', async () => {
    access.fails = true;
    const page = await open();

    await page.get('[data-ban-lift="u1"]').trigger('click');
    await flushPromises();

    expect(calls).toHaveLength(1);
    expect(page.find('[data-ban="u1"]').exists()).toBe(true);
  });

  it('shows the list without an unban button when the action is not held', async () => {
    access.unban = false;
    const page = await open();

    expect(page.findAll('[data-ban]')).toHaveLength(2);
    expect(page.find('[data-ban-lift]').exists()).toBe(false);
  });

  it('says so when nobody is banned', async () => {
    store.bans = [];
    const page = await open();

    expect(page.text()).toContain('Nobody is banned in your channel.');
  });
});
