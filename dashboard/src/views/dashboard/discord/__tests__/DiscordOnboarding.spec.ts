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
};

const held = vi.hoisted(() => ({
  botInstalled: false,
  // What the bot answers when asked whether it is in the server.
  presence: false,
  calls: [] as Call[],
}));

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
      held.calls.push({ method: 'GET', path });

      if (path.endsWith('/discord/connections'))
        return {
          data: { data: [{
            id: 'conn1',
            broadcasterId: 'c1',
            guildId: 'g1',
            guildName: 'Stoney Guild',
            botInstalled: held.botInstalled,
            serverConsentStatus: 'approved',
            streamerEnabled: true,
            isLinkActive: true,
          }] },
          response: { status: 200 },
        };

      return { data: { data: [] }, response: { status: 200 } };
    },
    POST: async (path: string) => {
      held.calls.push({ method: 'POST', path });

      held.botInstalled = path.endsWith('/presence')
        ? held.presence
        : held.botInstalled;

      return {
        data: { data: { connectionId: 'conn1', botInstalled: held.presence } },
        response: { status: 200 },
      };
    },
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.botInstalled = false;
  held.presence = false;
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

const presenceCalls = (): Call[] =>
  held.calls.filter((call) => call.method === 'POST' && call.path.endsWith('/presence'));

describe('the Discord page when the linked server has no bot in it', () => {
  it('asks the bot whether it is in the server when the page opens', async () => {
    await open();

    expect(presenceCalls()).toHaveLength(1);
    expect(presenceCalls()[0]?.path).toContain('/discord/connections/{connectionId}/presence');
  });

  it('leads with one action that adds the bot, and sets up nothing else until the bot is there', async () => {
    await open();

    const add = document.body.querySelector<HTMLAnchorElement>('[data-add-bot]');

    expect(add?.getAttribute('href')).toContain('/channels/c1/integrations/discord/callback/start');
    expect(document.body.querySelector('[data-rule-row]')).toBeNull();
    expect(document.body.querySelector('[data-add-role]')).toBeNull();
  });

  it('checks again on demand and opens the notification setup once the bot answers it is in', async () => {
    await open();

    held.presence = true;
    document.body.querySelector<HTMLButtonElement>('[data-check-again]')?.click();
    await flushPromises();
    await new Promise((done) => setTimeout(done, 0));
    await flushPromises();

    expect(presenceCalls()).toHaveLength(2);
    expect(document.body.querySelector('[data-add-bot]')).toBeNull();
    expect(document.body.querySelectorAll('[data-rule-row]').length).toBeGreaterThan(0);
  });
});

describe('the Discord page when the bot is in the server', () => {
  it('shows the notification setup, and offers to use another server', async () => {
    held.botInstalled = true;
    held.presence = true;
    await open();

    expect(document.body.querySelectorAll('[data-rule-row]').length).toBeGreaterThan(0);
    expect(document.body.querySelector('[data-check-again]')).toBeNull();

    expect(document.body.querySelector('[data-change-server]')?.getAttribute('href')).toContain(
      '/integrations/discord/callback/start',
    );
  });
});
