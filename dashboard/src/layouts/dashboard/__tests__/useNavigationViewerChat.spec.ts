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
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { useNavigation, type Navigation } from '../useNavigation';

// What the bot says the person may read.
const held = vi.hoisted(() => ({ keys: [] as string[] }));

// The bot has not told the platform keys yet, so no admin entry is hidden.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: false }, can: () => false }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: computed(() => true),
    can: (key: string) => held.keys.includes(key),
  }),
}));

const page = defineComponent({ render: () => h('div') });

const built: string[] = ['dashboard-home', 'dashboard-chat', 'dashboard-viewer-chat'];

async function open(): Promise<Navigation> {
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: built.map((name) => ({
      name,
      path: `/${name}`,
      component: page,
    })),
  });

  await router.push('/dashboard-home');
  let navigation: Navigation | undefined;

  const host = defineComponent({
    setup() {
      navigation = useNavigation();

      return () => h('div');
    },
  });

  mount(host, { global: { plugins: [router] } });
  await flushPromises();

  return navigation as Navigation;
}

const labels = (navigation: Navigation): string[] =>
  navigation.entries.value.map((entry) => entry.label);

beforeEach(() => {
  localStorage.clear();
});

describe('the viewer chat entry', () => {
  it('shows for a person who may read the viewer chat but not the operator chat', async () => {
    held.keys = ['dashboard:read', 'chat:viewer:read'];

    expect(labels(await open())).toEqual(['home', 'viewerChat']);
  });

  it('gives way to the operator chat when the person may read both', async () => {
    held.keys = ['dashboard:read', 'chat:read', 'chat:viewer:read'];

    expect(labels(await open())).toEqual(['home', 'chat']);
  });

  it('is hidden when the person may read neither chat', async () => {
    held.keys = ['dashboard:read'];

    expect(labels(await open())).toEqual(['home']);
  });
});
