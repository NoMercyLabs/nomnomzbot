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
import { createMemoryHistory, createRouter } from 'vue-router';

import ProfileView from '@/components/layout/profile/ProfileView.vue';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';

const held = vi.hoisted(() => ({ keys: [] as string[], banned: false }));

const member = (): Record<string, unknown> => ({
  id: 't-dave',
  internalUserId: 'u-dave',
  username: 'dave',
  displayName: 'Dave',
  profileImageUrl: null,
  messageCount: 3,
  watchHours: 1,
  commandsUsed: 1,
  trustLevel: 'viewer',
  isBanned: held.banned,
  firstSeen: '2026-01-02T10:00:00Z',
  lastSeen: '2026-10-12T10:00:00Z',
  banHistory: [],
  recentActivity: [],
  isVip: false,
});

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => held.keys.includes(key),
  }),
}));

vi.mock('@/api/auth', () => ({
  useCurrentUser: () => ({ data: ref({ username: 'owner' }) }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1'), current: ref({ login: 'owner' }) }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    // The voice read answers 404: this person speaks with the channel voice.
    GET: async (path: string) =>
      path.endsWith('/voice')
        ? {
            data: undefined,
            response: { status: 404, ok: false },
          }
        : {
            data: { data: path.endsWith('/community/{userId}')
              ? member()
              : null },
            response: { status: 200, ok: true },
          },
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.keys = [];
  held.banned = false;
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:pathMatch(.*)*', component: { template: '<div />' } }],
  });

  await router.push('/app/people');

  mounted = mount(ProfileView, {
    attachTo: document.body,
    props: { userId: 't-dave' },
    global: { plugins: [i18n, router, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

const actions = [
  ['[data-profile-shoutout]', 'moderation:shoutout'],
  ['[data-profile-vip]', 'moderation:vip'],
  ['[data-profile-warn]', 'moderation:warn'],
  ['[data-profile-timeout]', 'moderation:ban'],
  ['[data-profile-ban]', 'moderation:ban'],
  ['[data-profile-voice-change]', 'tts:uservoice:write'],
] as const;

describe('A person on the People page shows only the actions the viewer may use', () => {
  it('shows no action without any key', async () => {
    const view = await open([]);

    expect(view.find('[data-slot="profile-view"]').exists()).toBe(true);

    const shown = actions.filter(([selector]) => view.find(selector).exists());

    expect(shown).toEqual([]);
  });

  it.each(actions)('shows %s only with %s', async (selector, key) => {
    const view = await open([key, 'tts:voice:read']);

    expect(view.find(selector).exists()).toBe(true);

    const others = actions.filter(([, otherKey]) => otherKey !== key);
    const shown = others.filter(([other]) => view.find(other).exists());

    expect(shown).toEqual([]);
  });

  it('shows Unban, and no Ban or Timeout, for a banned person with moderation:unban', async () => {
    held.banned = true;

    const view = await open(['moderation:unban', 'moderation:ban']);

    expect(view.find('[data-profile-unban]').exists()).toBe(true);
    expect(view.find('[data-profile-ban]').exists()).toBe(false);
    expect(view.find('[data-profile-timeout]').exists()).toBe(false);
  });

  it('hides Unban from a banned person without moderation:unban', async () => {
    held.banned = true;

    const view = await open(['moderation:ban']);

    expect(view.find('[data-profile-unban]').exists()).toBe(false);
    expect(view.find('[data-profile-ban]').exists()).toBe(false);
  });
});
