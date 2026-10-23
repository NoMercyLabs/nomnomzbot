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

import DiscordView from '../DiscordView.vue';
import en from '../i18n/en.json';

type Call = {
  method: string;
  path: string;
  body: unknown;
};

const connection = {
  id: 'conn1',
  broadcasterId: 'c1',
  guildId: 'g1',
  guildName: 'Stoney Guild',
  botInstalled: true,
  serverConsentStatus: 'approved',
  streamerEnabled: true,
  isLinkActive: true,
};

const posts = [
  {
    id: 'p1',
    notificationConfigId: 'rule1',
    triggerType: 'go_live',
    dedupeKey: 'k1',
    streamId: null,
    postedMessageId: 'm1',
    status: 'sent',
    error: null,
    dispatchedAt: '2026-10-20T10:00:00Z',
  },
  {
    id: 'p2',
    notificationConfigId: 'rule1',
    triggerType: 'new_clip',
    dedupeKey: 'k2',
    streamId: null,
    postedMessageId: null,
    status: 'failed',
    error: 'Missing Access',
    dispatchedAt: '2026-10-20T11:00:00Z',
  },
];

const held = vi.hoisted(() => ({ linked: true, calls: [] as Call[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      if (path.endsWith('/discord/connections'))
        return { data: { data: held.linked
          ? [connection]
          : [] }, response: { status: 200 } };

      if (path.endsWith('/dispatch-log'))
        return { data: { data: posts }, response: { status: 200 } };

      return { data: { data: [] }, response: { status: 200 } };
    },
    PUT: async (path: string, options?: { body?: unknown }) => {
      held.calls.push({
        method: 'PUT',
        path,
        body: options?.body,
      });

      return { data: { data: null }, response: { status: 200 } };
    },
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.linked = true;
  held.calls.length = 0;
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  mounted = mount(DiscordView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

describe('the Discord page with no server linked', () => {
  it('explains what the bot does and offers one link that starts the Discord install', async () => {
    held.linked = false;
    await open();

    const link = document.body.querySelector<HTMLAnchorElement>('[data-add-bot]');

    expect(link?.getAttribute('href')).toMatch(
      /\/api\/v1\/channels\/c1\/integrations\/discord\/callback\/start$/,
    );

    expect(document.body.querySelector('[data-bot-does]')?.textContent).toContain(
      'Posts a message in a Discord channel',
    );

    expect(document.body.querySelector('[data-rule-row]')).toBeNull();
  });
});

describe('the Discord page with a server linked', () => {
  it('sends the streamer on or off choice to the server as the enabled flag', async () => {
    await open();

    document.body.querySelector<HTMLElement>('[data-streamer-enabled] button')!.click();
    await flushPromises();

    expect(held.calls).toEqual([
      {
        method: 'PUT',
        path: '/api/v{version}/channels/{channelId}/discord/connections/{connectionId}/streamer-enabled',
        body: { enabled: false },
      },
    ]);
  });

  it('lists what the bot posted with the trigger, the result and the error', async () => {
    await open();

    const rows = [...document.body.querySelectorAll('[data-post-row]')];

    expect(rows).toHaveLength(2);
    expect(rows[0]?.textContent).toContain('Stream goes live');
    expect(rows[0]?.textContent).toContain('Posted');
    expect(rows[1]?.textContent).toContain('New clip');
    expect(rows[1]?.textContent).toContain('Missing Access');
  });
});
