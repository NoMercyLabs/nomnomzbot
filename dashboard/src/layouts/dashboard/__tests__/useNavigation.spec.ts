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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { useNavigation, type Navigation } from '../useNavigation';

// What the bot says the person may read; null means it has not answered yet.
const held = vi.hoisted(() => ({ keys: null as string[] | null }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: computed(() => held.keys !== null),
    can: (key: string) => held.keys?.includes(key) === true,
  }),
}));

const page = defineComponent({ render: () => h('div') });

function makeRouter(names: string[]): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: names.map((name: string) => ({
      name,
      path: `/${name}`,
      component: page,
    })),
  });
}

// Mounts a host that exposes the navigation the sidebar would read.
async function open(router: Router, path: string): Promise<Navigation> {
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

  return navigation as Navigation;
}

const built: string[] = [
  'dashboard-home',
  'dashboard-chat',
  'dashboard-stream',
  'dashboard-commands',
  'dashboard-triggers-timers',
];

// The open state is shared by every sidebar; a storage event makes it read the cleared storage again.
beforeEach(() => {
  held.keys = null;
  localStorage.clear();

  window.dispatchEvent(
    new StorageEvent('storage', { key: 'nnz.nav.folds', storageArea: localStorage }),
  );
});

afterEach(() => localStorage.clear());

describe('the dashboard navigation', () => {
  it('shows every page until the bot has answered, then only the pages the person may read', async () => {
    const router = makeRouter(built);

    const unknown = await open(router, '/dashboard-home');

    expect(unknown.entries.value.map((entry) => entry.label)).toEqual([
      'home',
      'chat',
      'stream',
      'commands',
      'triggersTimers',
    ]);

    held.keys = ['dashboard:read', 'chat:read'];
    const moderator = await open(router, '/dashboard-home');

    expect(moderator.entries.value.map((entry) => entry.label)).toEqual(['home', 'chat']);
    expect(moderator.groups.value.map((group) => group.key)).toEqual(['home', 'live']);
  });

  it('hides an entry whose page has no route and drops a group left empty', async () => {
    const navigation = await open(makeRouter(built), '/dashboard-home');

    const labels = navigation.entries.value.map((entry) => entry.label);

    expect(labels).toEqual(['home', 'chat', 'stream', 'commands', 'triggersTimers']);
    expect(navigation.groups.value.map((group) => group.key)).toEqual(['home', 'live', 'build']);
  });

  it('opens the group of the open page and folds the others', async () => {
    const navigation = await open(makeRouter(built), '/dashboard-chat');

    const live = navigation.groups.value.find((group) => group.key === 'live');
    const build = navigation.groups.value.find((group) => group.key === 'build');

    expect(live).toMatchObject({ open: true, holdsOpenPage: true });

    expect(build).toMatchObject({
      open: false,
      holdsOpenPage: false,
      foldable: true,
    });
  });

  it('keeps a group the person opened open after the frame mounts again, and a folded one folded', async () => {
    const router = makeRouter(built);
    const first = await open(router, '/dashboard-chat');
    const build = first.groups.value.find((group) => group.key === 'build')!;

    first.toggle(build);
    await flushPromises();

    expect(JSON.parse(localStorage.getItem('nnz.nav.folds') ?? '{}')).toMatchObject({
      build: true,
    });

    const second = await open(router, '/dashboard-chat');

    expect(second.groups.value.find((group) => group.key === 'build')?.open).toBe(true);

    second.toggle(second.groups.value.find((group) => group.key === 'build')!);
    await flushPromises();
    const third = await open(router, '/dashboard-chat');

    expect(third.groups.value.find((group) => group.key === 'build')?.open).toBe(false);

    expect(JSON.parse(localStorage.getItem('nnz.nav.folds') ?? '{}')).toMatchObject({
      build: false,
    });
  });

  it('keeps a group open after moving to a page in another group, also after a reload', async () => {
    const router = makeRouter(built);
    const navigation = await open(router, '/dashboard-chat');

    await router.push('/dashboard-commands');
    await flushPromises();

    expect(navigation.groups.value.find((group) => group.key === 'live')?.open).toBe(true);
    expect(navigation.groups.value.find((group) => group.key === 'build')?.open).toBe(true);
    const reloaded = await open(router, '/dashboard-home');

    expect(
      reloaded.groups.value.filter((group) => group.foldable).map((group) => group.open),
    ).toEqual([true, true]);
  });

  it('never folds the group that holds the open page', async () => {
    const navigation = await open(makeRouter(built), '/dashboard-chat');

    navigation.toggle(navigation.groups.value.find((group) => group.key === 'live')!);
    await flushPromises();

    expect(navigation.groups.value.find((group) => group.key === 'live')?.open).toBe(true);
  });
});
