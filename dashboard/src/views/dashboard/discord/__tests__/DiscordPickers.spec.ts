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
import { dashboardNav } from '@/layouts/dashboard/navigation';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';

import DiscordView from '../DiscordView.vue';
import en from '../i18n/en.json';
import { dashboardDiscordRoute } from '../route';

const reason = 'The Discord bot is not set up on this bot server yet. The person who runs the bot server must add the Discord bot token.';

function expectPlain(text: string | null | undefined): void {
  expect(text).toContain(reason);
  expect(text).not.toContain('Discord:BotToken');
  expect(text).not.toContain('Try again');
}

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
        return { data: { data: [connection] }, response: { status: 200 } };

      if (path.endsWith('/guild/channels/postable') || path.endsWith('/guild/roles'))
        return {
          error: {
            status: 'error',
            message: 'The Discord bot token is not configured on this server. Set Discord:BotToken (or DISCORD_BOT_TOKEN).',
            code: 'DISCORD_BOT_TOKEN_NOT_CONFIGURED',
          },
          response: { status: 500 },
        };

      return { data: { data: [] }, response: { status: 200 } };
    },
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
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

describe('the Discord pickers when the server cannot read Discord', () => {
  it('the rule editor names the reason instead of an empty channel and role list', async () => {
    await open();

    document.body.querySelector<HTMLElement>('[data-edit="stream.online"]')!.click();
    await flushPromises();

    const alerts = [...document.body.querySelectorAll('[data-rule-editor] [data-picker-error]')];

    // One line under the channel picker and one under the ping role picker.
    expect(alerts).toHaveLength(2);

    for (const alert of alerts) {
      expect(alert.getAttribute('role')).toBe('alert');
      expectPlain(alert.textContent);
    }
  });

  it('the live role panel names the reason instead of an empty role list', async () => {
    await open();

    expectPlain(document.body.querySelector('[data-live-role-panel] [data-picker-error]')?.textContent);
  });

  it('the self-assign role editor names the reason', async () => {
    await open();

    document.body.querySelector<HTMLElement>('[data-add-role]')!.click();
    await flushPromises();

    expectPlain(document.body.querySelector('[data-notify-role-editor] [data-picker-error]')?.textContent);
  });
});

describe('where the Discord page lives', () => {
  it('opens under the settings with the settings sidebar', () => {
    expect(dashboardDiscordRoute.path).toBe('settings/discord');
    expect(dashboardDiscordRoute.name).toBe('dashboard-settings-discord');
    expect(dashboardDiscordRoute.meta.navMode).toBe('settings');
  });

  it('is listed in the settings connections, not in the main sidebar', () => {
    const main = dashboardNav.flatMap((group) => group.entries.map((entry) => entry.route));
    const connections = settingsNav.find((group) => group.key === 'settings.connections');

    expect(main.some((route) => route.includes('discord'))).toBe(false);
    expect(connections?.entries.map((entry) => entry.route)).toContain('dashboard-settings-discord');
  });
});
