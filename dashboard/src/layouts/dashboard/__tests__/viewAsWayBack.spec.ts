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
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router';

import { viewAs } from '@/api/viewAs';
import { i18n } from '@/i18n';
import { channelVariants } from '@/router/channelRoutes';

import ProfileMenu from '../ProfileMenu.vue';
import { useNavigation, type Navigation } from '../useNavigation';

// What the bot says the person may read; a viewer holds no dashboard key.
const held = vi.hoisted(() => ({ keys: [] as string[] }));

vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: false }, can: () => false }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channelId: ref('c1'),
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
  }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: computed(() => true),
    can: (key: string) => held.keys.includes(key),
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

const page = defineComponent({ render: () => h('div') });

const pages: RouteRecordRaw[] = [
  {
    name: 'dashboard-home',
    path: 'home',
    component: page,
    meta: { action: 'dashboard:read' },
  },
  {
    name: 'dashboard-viewer-chat',
    path: 'viewer-chat',
    component: page,
    meta: { action: 'chat:viewer:read' },
  },
  {
    name: 'dashboard-settings',
    path: 'settings',
    component: page,
    meta: { navMode: 'settings' },
  },
  {
    name: 'dashboard-admin-tenants',
    path: 'admin/tenants',
    component: page,
    meta: { navMode: 'admin' },
  },
];

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app',
        component: page,
        children: pages,
      },
      {
        path: '/app/u/:channelName',
        component: page,
        children: channelVariants(pages),
      },
    ],
  });
}

async function openAt(path: string): Promise<{ navigation: Navigation; router: Router }> {
  const router = makeRouter();

  await router.push(path);
  let navigation: Navigation | undefined;
  const host = defineComponent({
    setup() {
      navigation = useNavigation();

      return () => h('div');
    },
  });

  mount(host, { global: { plugins: [router] } });
  await flushPromises();

  return {
    navigation: navigation as Navigation,
    router,
  };
}

beforeEach(() => {
  held.keys = [];
  viewAs.value = '';
});

afterEach(() => {
  viewAs.value = '';
  localStorage.clear();
});

describe('The sidebar always keeps a way back', () => {
  it('leads a viewer in the settings sidebar to the first page the viewer may open', async () => {
    held.keys = ['chat:viewer:read'];
    const { navigation, router } = await openAt('/app/settings');
    const back = navigation.entries.value.find((entry) => entry.label === 'back.dashboard');

    expect(back).toBeDefined();

    const target = router.resolve(navigation.linkTo(back!));

    expect(target.name).toBe('u:dashboard-viewer-chat');
    expect(target.meta.action).toBe('chat:viewer:read');
  });

  it('leads a person who may read the dashboard home back to the home page', async () => {
    held.keys = ['dashboard:read', 'chat:viewer:read'];
    const { navigation, router } = await openAt('/app/settings');
    const back = navigation.entries.value.find((entry) => entry.label === 'back.dashboard');

    expect(router.resolve(navigation.linkTo(back!)).name).toBe('u:dashboard-home');
  });

  it('keeps the way back in the admin sidebar too', async () => {
    held.keys = ['chat:viewer:read'];
    const { navigation, router } = await openAt('/app/admin/tenants');
    const back = navigation.entries.value.find((entry) => entry.label === 'back.admin');

    expect(back).toBeDefined();
    expect(router.resolve(navigation.linkTo(back!)).name).toBe('u:dashboard-viewer-chat');
  });
});

describe('The sidebar footer names the viewed role', () => {
  async function footerRole(): Promise<string> {
    const menu = mount(ProfileMenu, { global: { plugins: [i18n, makeRouter()] } });

    await flushPromises();

    return menu.find('[data-slot="profile-menu"]').text();
  }

  it('shows Broadcaster for the owner and Viewer or Moderator while previewing', async () => {
    expect(await footerRole()).toContain('Broadcaster');

    viewAs.value = 'viewer';
    const asViewer = await footerRole();

    expect(asViewer).toContain('Viewer');
    expect(asViewer).not.toContain('Broadcaster');

    viewAs.value = 'moderator';

    expect(await footerRole()).toContain('Moderator');
  });
});
