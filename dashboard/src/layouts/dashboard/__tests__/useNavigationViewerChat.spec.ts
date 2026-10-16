// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { useNavigation, type Navigation } from '../useNavigation';

// What the bot says the person may read.
const held = vi.hoisted(() => ({ keys: [] as string[] }));

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
    routes: built.map((name: string) => ({
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
