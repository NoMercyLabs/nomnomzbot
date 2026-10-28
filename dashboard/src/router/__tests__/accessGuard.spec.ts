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

import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent } from 'vue';
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router';

import { channelAccessKey, type ResolvedAccess } from '@/api/access';
import type { ChannelSummary } from '@/api/channels';
import { queryClient } from '@/api/queryClient';
import { chosenChannelId } from '@/composables/useCurrentChannel';

import { accessGuard } from '../accessGuard';
import { channelVariants } from '../channelRoutes';

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<unknown>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

const page = defineComponent({ render: () => null });
const pages: RouteRecordRaw[] = [
  {
    path: '',
    name: 'dashboard-home',
    component: page,
    meta: { action: 'dashboard:read' },
  },
  {
    path: 'viewer-chat',
    name: 'dashboard-viewer-chat',
    component: page,
    meta: { action: 'chat:viewer:read' },
  },
  {
    path: 'commands',
    name: 'dashboard-commands',
    component: page,
    meta: { action: 'commands:read' },
  },
  {
    path: 'stream',
    name: 'dashboard-stream',
    component: page,
    meta: { action: 'stream:read' },
  },
  {
    path: 'you',
    name: 'dashboard-you',
    component: page,
    meta: { open: true },
  },
  {
    path: 'plain',
    name: 'dashboard-plain',
    component: page,
  },
];

function makeRouter(): Router {
  const router = createRouter({
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
      {
        path: '/me',
        component: page,
        children: [
          {
            path: ':channel',
            name: 'viewer-me',
            component: page,
          },
          {
            path: ':channel/chat',
            name: 'viewer-chat',
            component: page,
            meta: { action: 'chat:viewer:read' },
          },
        ],
      },
    ],
  });

  router.beforeEach(accessGuard(router));

  return router;
}

function channel(id: string, login: string, role: string): ChannelSummary {
  return {
    id,
    login,
    displayName: login,
    isLive: false,
    role,
    provider: 'twitch',
  };
}

function holding(keys: string[]): ResolvedAccess {
  return {
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
}

function seedAccess(channelId: string, keys: string[]): void {
  queryClient.setQueryData(channelAccessKey(channelId), holding(keys));
}

beforeEach(() => {
  queryClient.clear();
  http.GET.mockReset();
  chosenChannelId.value = 'c1';

  queryClient.setQueryData(['channels', 'mine'], [
    channel('c1', 'me', 'broadcaster'),
    channel('c2', 'friend', 'moderator'),
  ]);

  queryClient.setQueryData(['channels', 'moderated'], []);
});

describe('a page that names an action', () => {
  it('sends a person without the key to home when they may read the dashboard', async () => {
    seedAccess('c1', ['dashboard:read', 'stream:read']);
    const router = makeRouter();

    await router.push('/app/commands');

    expect(router.currentRoute.value.name).toBe('dashboard-home');
  });

  it('sends a person without the key and without home to the viewer chat', async () => {
    seedAccess('c1', ['chat:viewer:read', 'stream:read']);
    const router = makeRouter();

    await router.push('/app/commands');

    expect(router.currentRoute.value.name).toBe('dashboard-viewer-chat');
  });

  it('sends a person with neither home nor viewer chat to the first sidebar page they may read', async () => {
    seedAccess('c1', ['stream:read']);
    const router = makeRouter();

    await router.push('/app/commands');

    expect(router.currentRoute.value.name).toBe('dashboard-stream');
  });

  it('opens the page for a person who holds the key', async () => {
    seedAccess('c1', ['commands:read']);
    const router = makeRouter();

    await router.push('/app/commands');

    expect(router.currentRoute.value.name).toBe('dashboard-commands');
  });

  it('checks the channel named in the address, not the one being managed', async () => {
    seedAccess('c1', ['commands:read', 'dashboard:read']);
    seedAccess('c2', ['dashboard:read']);
    const router = makeRouter();

    await router.push('/app/u/friend/commands');

    expect(router.currentRoute.value.name).toBe('u:dashboard-home');
    expect(router.currentRoute.value.params.channelName).toBe('friend');
  });

  it('keeps the page when the access cannot be read', async () => {
    http.GET.mockResolvedValue({ error: { detail: 'down' }, response: { ok: false, status: 500 } });
    const router = makeRouter();

    await router.push('/app/commands');

    expect(router.currentRoute.value.name).toBe('dashboard-commands');
    expect(http.GET).toHaveBeenCalledTimes(1);
  });

  it('keeps the page when the named channel is not set up here', async () => {
    const router = makeRouter();

    await router.push('/app/u/stranger/commands');

    expect(router.currentRoute.value.name).toBe('u:dashboard-commands');
    expect(http.GET).not.toHaveBeenCalled();
  });
});

describe('a viewer page named by a channel in the address', () => {
  it('checks the channel in the address, not the one being managed', async () => {
    seedAccess('c1', ['chat:viewer:read']);
    seedAccess('c2', ['dashboard:read']);
    const router = makeRouter();

    await router.push('/me/friend/chat');

    expect(router.currentRoute.value.name).toBe('viewer-me');
    expect(router.currentRoute.value.params.channel).toBe('friend');
  });

  it('opens the page when the person holds the key in the channel in the address', async () => {
    seedAccess('c1', ['dashboard:read']);
    seedAccess('c2', ['chat:viewer:read']);
    const router = makeRouter();

    await router.push('/me/friend/chat');

    expect(router.currentRoute.value.name).toBe('viewer-chat');
  });
});

describe('a page that names no action', () => {
  it('opens a page marked open without reading the access', async () => {
    const router = makeRouter();

    await router.push('/app/you');

    expect(router.currentRoute.value.name).toBe('dashboard-you');
    expect(http.GET).not.toHaveBeenCalled();
  });

  it('opens a page without meta without reading the access', async () => {
    const router = makeRouter();

    await router.push('/app/plain');

    expect(router.currentRoute.value.name).toBe('dashboard-plain');
    expect(http.GET).not.toHaveBeenCalled();
  });
});
