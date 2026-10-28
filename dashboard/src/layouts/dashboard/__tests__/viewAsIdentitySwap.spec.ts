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

import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { viewAs } from '@/api/viewAs';
import { i18n } from '@/i18n';

import ChannelSwitch from '../ChannelSwitch.vue';
import FrameActions from '../FrameActions.vue';
import ProfileMenu from '../ProfileMenu.vue';
import { useReadableRedirect } from '../useReadableRedirect';

// What the bot says the person may do; null means it has not answered yet.
const held = vi.hoisted(() => ({ keys: null as string[] | null }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channelId: ref('c1'),
    channels: computed(() => []),
    current: computed(() => ({
      login: 'stoney_eagle',
      displayName: 'Stoney_Eagle',
      profileImageUrl: null,
      isLive: false,
      kind: 'own',
      channelId: 'c1',
      twitchId: null,
      chatColor: null,
    })),
    isPending: computed(() => false),
    choose: vi.fn<() => Promise<void>>(),
  }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: computed(() => held.keys !== null),
    can: (key: string) => held.keys?.includes(key) === true,
  }),
}));

vi.mock('@/api/auth', () => ({
  signOut: vi.fn<() => Promise<void>>(),
  useCurrentUser: () => ({
    data: ref({
      displayName: 'Stoney_Eagle',
      isAdmin: true,
      platformPermissions: [],
      profileImageUrl: null,
    }),
  }),
}));

vi.mock('../ShieldToggle.vue', () => ({
  default: defineComponent({ render: () => h('div', { 'data-slot': 'shield' }) }),
}));

vi.mock('../ErrorInbox.vue', () => ({
  default: defineComponent({ render: () => h('div', { 'data-slot': 'inbox' }) }),
}));

const page = defineComponent({ render: () => h('div') });

function makeRouter(names: string[]): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: names.map((name) => ({
      name,
      path: `/${name}`,
      component: page,
    })),
  });
}

beforeEach(() => {
  held.keys = null;
  viewAs.value = '';
});

afterEach(() => {
  viewAs.value = '';
  localStorage.clear();
});

describe('View as is a full identity swap', () => {
  it('hides the View as entries and Platform admin from the profile menu while previewing', async () => {
    const router = makeRouter(['dashboard-admin-channels', 'dashboard-settings']);

    const mountMenu = async (): Promise<string> => {
      const menu = mount(ProfileMenu, {
        attachTo: document.body,
        global: { plugins: [i18n, router] },
      });

      await menu.find('[data-slot="profile-menu"]').trigger('click');
      await flushPromises();

      return document.body.textContent ?? '';
    };

    const full = await mountMenu();

    expect(full).toContain('Platform admin');
    expect(full).toContain('View as');

    document.body.innerHTML = '';
    viewAs.value = 'viewer';
    const previewed = await mountMenu();

    expect(previewed).not.toContain('Platform admin');
    expect(previewed).not.toContain('View as');
  });

  it('lists no other channels while previewing', () => {
    viewAs.value = 'moderator';
    const switcher = mount(ChannelSwitch, { global: { plugins: [i18n] } });

    expect(switcher.find('[data-slot="channel-switch"]').exists()).toBe(false);
    expect(switcher.find('[data-slot="channel-switch-static"]').text()).toContain('Stoney_Eagle');
  });

  it('shows the shield and the inbox only to a person who holds the moderation action', () => {
    held.keys = [];
    const viewer = mount(FrameActions);

    expect(viewer.find('[data-slot="shield"]').exists()).toBe(false);
    expect(viewer.find('[data-slot="inbox"]').exists()).toBe(false);

    held.keys = ['moderation:chat:settings:read'];
    const moderator = mount(FrameActions);

    expect(moderator.find('[data-slot="shield"]').exists()).toBe(true);
    expect(moderator.find('[data-slot="inbox"]').exists()).toBe(true);
  });

  it('sends a page the person may not read to the first page they may', async () => {
    held.keys = ['chat:read'];
    const router = makeRouter(['dashboard-home', 'dashboard-chat']);

    await router.push('/dashboard-home');
    const host = defineComponent({
      setup() {
        useReadableRedirect();

        return () => h('div');
      },
    });

    mount(host, { global: { plugins: [router] } });
    await flushPromises();

    expect(router.currentRoute.value.name).toBe('dashboard-chat');
  });
});
