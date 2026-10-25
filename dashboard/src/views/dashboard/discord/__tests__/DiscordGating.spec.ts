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

const role = {
  id: 'r1',
  discordRoleId: 'dr1',
  roleName: 'Live pings',
  optInCount: 4,
};

const held = vi.hoisted(() => ({ keys: [] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => held.keys.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      const byPath: Record<string, unknown[]> = {
        '/api/v{version}/channels/{channelId}/discord/connections': [connection],
        '/api/v{version}/channels/{channelId}/discord/connections/{connectionId}/roles': [role],
        '/api/v{version}/channels/{channelId}/discord/triggers': [
          {
            name: 'go_live',
            labelKey: 'dashboard.discord.triggers.go_live',
            placeholders: [],
          },
        ],
      };

      return {
        data: { data: byPath[path] ?? [] },
        response: { status: 200, ok: true },
      };
    },
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.keys = [];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(DiscordView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

describe('DiscordView shows only what the person may use', () => {
  it('shows no connection, rules or roles without any key', async () => {
    const view = await open([]);

    expect(view.find('[data-connection]').exists()).toBe(false);
    expect(view.find('[data-rule-row]').exists()).toBe(false);
    expect(view.find('[data-notify-role-row]').exists()).toBe(false);
  });

  it('shows the reads but no edit or add control with the read keys alone', async () => {
    const view = await open([
      'discord:connection:read',
      'discord:config:read',
      'discord:role:read',
    ]);

    expect(view.find('[data-connection]').exists()).toBe(true);
    expect(view.find('[data-rule-row]').exists()).toBe(true);
    expect(view.find('[data-notify-role-row]').exists()).toBe(true);
    expect(view.find('[data-edit]').exists()).toBe(false);
    expect(view.find('[data-add-role]').exists()).toBe(false);
    expect(view.find('[data-edit-role]').exists()).toBe(false);
  });

  it('shows the rule edit control with discord:config:write', async () => {
    const view = await open([
      'discord:connection:read',
      'discord:config:read',
      'discord:config:write',
    ]);

    expect(view.find('[data-edit]').exists()).toBe(true);
    expect(view.find('[data-add-role]').exists()).toBe(false);
  });

  it('shows the role add and edit controls with discord:role:write', async () => {
    const view = await open(['discord:connection:read', 'discord:role:read', 'discord:role:write']);

    expect(view.find('[data-add-role]').exists()).toBe(true);
    expect(view.find('[data-edit-role]').exists()).toBe(true);
    expect(view.find('[data-edit]').exists()).toBe(false);
  });
});
