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

import { VueQueryPlugin } from '@tanstack/vue-query';
import { mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { channelAccessKey, type ResolvedAccess } from '@/api/access';
import { queryClient } from '@/api/queryClient';
import { chosenChannelId } from '@/composables/useCurrentChannel';

import { useTabbedPage, type TabSpec } from '../useTabbedPage';

vi.mock('@/api/client', () => ({
  client: { GET: vi.fn<() => void>(), use: (): undefined => undefined },
}));

const VIEWS = ['one', 'two', 'three'] as const;

type Tab = (typeof VIEWS)[number];

const ran = vi.hoisted(() => ({ count: 0 }));

const specs = [
  {
    value: 'one',
    label: 'One',
    read: 'x:one:read',
    action: {
      label: 'New one',
      write: 'x:one:write',
      run: (): void => {
        ran.count += 1;
      },
    },
  },
  {
    value: 'two',
    label: 'Two',
    read: 'x:two:read',
    count: 4,
  },
  {
    value: 'three',
    label: 'Three',
    disabledReason: 'Needs a plan',
  },
] as const satisfies readonly TabSpec<Tab>[];

function hold(keys: string[]): void {
  const access: ResolvedAccess = {
    userId: 'u1',
    broadcasterId: 'c1',
    effectiveLevel: 'Moderator',
    communityStanding: 'Viewer',
    communityLevel: 'Viewer',
    managementRole: 'Moderator',
    managementLevel: 'Moderator',
    permitRole: null,
    permitCapabilities: [],
    winningSource: 'management',
    heldActionKeys: keys,
  };

  queryClient.setQueryData(channelAccessKey('c1'), access);
}

type Opened = {
  router: Router;
  page: ReturnType<typeof useTabbedPage<Tab>>;
};

async function open(query: string): Promise<Opened> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/x', component: { template: '<div />' } }],
  });

  await router.push(`/app/x${query}`);

  let page!: ReturnType<typeof useTabbedPage<Tab>>;

  mount(
    defineComponent({
      setup: () => {
        page = useTabbedPage(VIEWS, () => specs);

        return () => h('div');
      },
    }),
    { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } },
  );

  return {
    router,
    page,
  };
}

beforeEach(() => {
  queryClient.clear();
  ran.count = 0;
  chosenChannelId.value = 'c1';

  queryClient.setQueryData(
    ['channels', 'mine'],
    [
      {
        id: 'c1',
        login: 'me',
        displayName: 'me',
        isLive: false,
        role: 'broadcaster',
        provider: 'twitch',
      },
    ],
  );

  queryClient.setQueryData(['channels', 'moderated'], []);
});

describe('useTabbedPage', () => {
  it('drops a tab whose read key the person does not hold and keeps an open tab', async () => {
    hold(['x:two:read']);
    const { page } = await open('');

    expect(page.tabs.value.map((tab) => tab.value)).toEqual(['two', 'three']);
  });

  it('maps count and a disabled reason onto the tab, and a plain tab carries neither', async () => {
    hold(['x:one:read', 'x:two:read']);
    const { page } = await open('');
    const [one, two, three] = page.tabs.value;

    expect(one).toMatchObject({ value: 'one', label: 'One' });
    expect(one?.disabled).not.toBe(true);
    expect(two).toMatchObject({ value: 'two', count: 4 });

    expect(three).toMatchObject({
      value: 'three',
      disabled: true,
      reason: 'Needs a plan',
    });
  });

  it('falls back to the first allowed tab when the link names a tab that is filtered out', async () => {
    hold(['x:two:read']);
    const { page } = await open('?view=one');

    expect(page.view.value).toBe('two');
  });

  it('opens the tab the link names when it is allowed', async () => {
    hold(['x:one:read', 'x:two:read']);
    const { page } = await open('?view=two');

    expect(page.view.value).toBe('two');
  });

  it('writes the picked tab into the link', async () => {
    hold(['x:one:read', 'x:two:read']);
    const { page, router } = await open('');

    page.view.value = 'two';
    await router.isReady();
    await new Promise((done) => setTimeout(done, 0));

    expect(router.currentRoute.value.query).toEqual({ view: 'two' });
  });

  it('gives the open tab action only when the person holds its write key', async () => {
    hold(['x:one:read']);
    const { page } = await open('');

    expect(page.action.value).toBeNull();
    expect(page.canAct.value).toBe(false);
  });

  it('gives the open tab action with its label and runs it when the write key is held', async () => {
    hold(['x:one:read', 'x:one:write']);
    const { page } = await open('');

    expect(page.action.value?.label).toBe('New one');
    expect(page.canAct.value).toBe(true);

    page.action.value?.run();

    expect(ran.count).toBe(1);
  });

  it('has no action on a tab that declares none', async () => {
    hold(['x:one:read', 'x:one:write', 'x:two:read']);
    const { page } = await open('?view=two');

    expect(page.action.value).toBeNull();
  });
});
