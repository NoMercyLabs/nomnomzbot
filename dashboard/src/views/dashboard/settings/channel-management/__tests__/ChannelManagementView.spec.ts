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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import { settingsChannelManagementRoute } from '../route';
import ChannelManagementView from '../ChannelManagementView.vue';
import en from '../i18n/en.json';

type Call = {
  method: string;
  path: string;
  target?: string;
};
type Options = {
  params?: { path?: { channelId?: string }; query?: { deleted?: boolean } };
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];
const bot = { joinedAt: null as string | null, previewFails: false };
const sideChannel = {
  id: 'c2',
  login: 'side_quest',
  displayName: 'Side Quest',
  deletedAt: '2026-10-10T08:30:00Z',
  permanentAfter: '2026-11-09T08:30:00Z',
};
const gone: { rows: unknown[] } = { rows: [] };

const preview = {
  channelName: 'stoney_eagle',
  blastRadius: {
    categories: [
      {
        categoryKey: 'blast_radius_category_commands',
        count: 12,
        sample: ['!hug'],
      },
      {
        categoryKey: 'blast_radius_category_timers',
        count: 0,
        sample: [],
      },
    ],
    isMinimum: true,
    totalReferences: 12,
  },
  externalConsequences: [
    {
      consequenceKey: 'channel_external_twitch_rewards',
      count: 3,
      sample: ['Hydrate'],
    },
  ],
  restoreWindowDays: 30,
  permanentAfterUtc: '2026-11-17T10:00:00Z',
};

const resetPreview = {
  channelName: 'stoney_eagle',
  blastRadius: {
    categories: [
      {
        categoryKey: 'blast_radius_category_reset_chat',
        count: 4,
        sample: ['chat.settings'],
      },
      {
        categoryKey: 'blast_radius_category_reset_music',
        count: 0,
        sample: [],
      },
    ],
    isMinimum: false,
    totalReferences: 4,
  },
};

const ok = (data?: unknown): Answer => ({
  data: { data },
  response: { status: 200, ok: true },
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({ method: 'GET', path });

      if (path.endsWith('/reset-preview')) {
        return ok(resetPreview);
      }

      if (options?.params?.query?.deleted === true) {
        return ok(gone.rows);
      }

      if (path.endsWith('/delete-preview')) {
        return bot.previewFails
          ? {
              error: { detail: 'Preview failed.' },
              response: { status: 500, ok: false },
            }
          : ok(preview);
      }

      return ok({ id: 'c1', botJoinedAt: bot.joinedAt });
    },
    POST: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        target: options?.params?.path?.channelId,
      });

      if (path.endsWith('/restore')) {
        gone.rows = gone.rows.filter((row) => row !== sideChannel);
      }

      if (path.endsWith('/join')) {
        bot.joinedAt = '2026-10-18T08:00:00Z';
      }

      if (path.endsWith('/leave')) {
        bot.joinedAt = null;
      }

      return { response: { status: 200, ok: true } };
    },
    DELETE: async (path: string): Promise<Answer> => {
      calls.push({ method: 'DELETE', path });

      return { response: { status: 204, ok: true } };
    },
  },
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => key === 'setup:write',
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;
  bot.joinedAt = null;
  bot.previewFails = false;
  gone.rows = [];
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(ChannelManagementView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const q = <T extends Element>(selector: string): T => document.body.querySelector<T>(selector) as T;

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

async function type(el: HTMLInputElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

const countOf = (method: string, suffix: string): number =>
  calls.filter((call) => call.method === method && call.path.endsWith(suffix)).length;

describe('Channel management', () => {
  it('is reached with the setup:write action', () => {
    expect(settingsChannelManagementRoute.meta.action).toBe('setup:write');
    expect(settingsChannelManagementRoute.name).toBe('dashboard-settings-channel-management');
  });

  it('lets the bot join, then reads the channel again and shows the bot inside', async () => {
    await open();

    expect(q('[data-slot="bot-presence"]').textContent).toContain('not in your channel');

    const reads = countOf('GET', '/channels/{channelId}');

    await click(q('[data-join]'));

    expect(countOf('POST', '/join')).toBe(1);
    expect(countOf('GET', '/channels/{channelId}')).toBe(reads + 1);
    expect(q('[data-slot="bot-presence"]').textContent).toContain('The bot is in your channel');
    expect(document.body.querySelector('[data-join]')).toBeNull();
  });

  it('asks before the bot leaves and sends nothing until the confirm', async () => {
    bot.joinedAt = '2026-10-18T08:00:00Z';
    await open();

    await click(q('[data-leave]'));

    expect(document.body.textContent).toContain('The bot stops answering commands in your chat.');
    expect(countOf('POST', '/leave')).toBe(0);

    await click(q('[data-confirm-action]'));

    expect(countOf('POST', '/leave')).toBe(1);
    expect(q('[data-slot="bot-presence"]').textContent).toContain('not in your channel');
  });

  it('shows what the delete destroys first and arms it only for the exact channel name', async () => {
    await open();

    await click(q('[data-delete]'));

    expect(countOf('GET', '/delete-preview')).toBe(1);

    const text = document.body.textContent;

    expect(text).toContain('12 commands');
    expect(text).not.toContain('timers');
    expect(text).toContain('minimum');
    expect(text).toContain('3 channel point rewards on Twitch stop working');
    expect(text).toContain('30 days');

    const confirm = q<HTMLButtonElement>('[data-confirm-action]');

    expect(confirm.disabled).toBe(true);

    await type(q<HTMLInputElement>('[data-confirm-typed]'), 'stoney');

    expect(q<HTMLButtonElement>('[data-confirm-action]').disabled).toBe(true);

    await click(q('[data-confirm-action]'));

    expect(countOf('DELETE', '/channels/{channelId}')).toBe(0);

    await type(q<HTMLInputElement>('[data-confirm-typed]'), 'stoney_eagle');
    await click(q('[data-confirm-action]'));

    expect(countOf('DELETE', '/channels/{channelId}')).toBe(1);
  });

  it('after the delete offers the way back until the removal date, and the restore sends the call', async () => {
    await open();

    await click(q('[data-delete]'));
    await type(q<HTMLInputElement>('[data-confirm-typed]'), 'stoney_eagle');
    await click(q('[data-confirm-action]'));

    expect(q('[data-slot="deleted-channel"]').textContent).toContain('The channel is deleted');
    expect(document.body.querySelector('[data-delete]')).toBeNull();

    await click(q('[data-restore]'));

    expect(countOf('POST', '/restore')).toBe(1);
    expect(document.body.querySelector('[data-slot="deleted-channel"]')).toBeNull();
    expect(document.body.querySelector('[data-delete]')).not.toBeNull();
  });

  it('shows the counted settings before a reset and sends nothing until the confirm', async () => {
    await open();

    await click(q('[data-reset]'));

    expect(countOf('GET', '/reset-preview')).toBe(1);
    expect(countOf('POST', '/reset')).toBe(0);

    const text = document.body.textContent;

    expect(text).toContain('4 chat settings');
    expect(text).not.toContain('music settings');

    await type(q<HTMLInputElement>('[data-confirm-typed]'), 'stoney_eagle');
    await click(q('[data-confirm-action]'));

    expect(countOf('POST', '/reset')).toBe(1);
  });

  it('lists another deleted channel with its removal date and restores that channel by its own id', async () => {
    gone.rows = [sideChannel];
    await open();

    const notice = q('[data-slot="deleted-channel"]');

    expect(notice.textContent).toContain('Side Quest');
    expect(document.body.querySelector('[data-delete]')).not.toBeNull();

    await click(q('[data-slot="deleted-channel"] [data-restore]'));

    const restores = calls.filter((call) => call.method === 'POST' && call.path.endsWith('/restore'));

    expect(restores).toEqual([{
      method: 'POST',
      path: '/api/v{version}/channels/{channelId}/restore',
      target: 'c2',
    }]);

    expect(document.body.querySelector('[data-slot="deleted-channel"]')).toBeNull();
  });

  it('offers no delete when the bot cannot count what it destroys', async () => {
    bot.previewFails = true;
    await open();

    await click(q('[data-delete]'));

    expect(document.body.querySelector('[data-confirm-typed]')).toBeNull();
    expect(q('[role="alert"]').textContent).not.toBe('');
    expect(countOf('DELETE', '/channels/{channelId}')).toBe(0);
  });
});
