// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent } from 'vue';
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router';

import { channelAccessKey, type ResolvedAccess } from '@/api/access';
import type { ChannelSummary, PublicChannel } from '@/api/channels';
import { queryClient } from '@/api/queryClient';

import { channelVariants } from '../channelRoutes';
import { rungGuard } from '../rungGuard';

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
    path: 'commands',
    name: 'dashboard-commands',
    component: page,
    meta: { action: 'commands:read' },
  },
  {
    path: 'viewer-chat',
    name: 'dashboard-viewer-chat',
    component: page,
    meta: { action: 'chat:viewer:read' },
  },
];

function makeRouter(): Router {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
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
            path: ':channel/twitch',
            name: 'viewer-external-chat',
            component: page,
          },
        ],
      },
    ],
  });

  router.beforeEach(rungGuard());

  return router;
}

function mine(id: string, login: string): ChannelSummary {
  return {
    id,
    login,
    displayName: login,
    isLive: false,
    role: 'broadcaster',
    provider: 'twitch',
  };
}

function listed(id: string, login: string, onBot: boolean): PublicChannel {
  return {
    id,
    login,
    displayName: login,
    isLive: false,
    provider: 'twitch',
    onBot,
  };
}

function holding(keys: string[]): ResolvedAccess {
  return {
    userId: 'u1',
    broadcasterId: 'c2',
    effectiveLevel: 'Viewer',
    communityStanding: 'Viewer',
    communityLevel: 'Viewer',
    managementRole: 'None',
    managementLevel: 'None',
    permitRole: null,
    permitCapabilities: [],
    winningSource: 'community',
    heldActionKeys: keys,
  };
}

function directoryHas(rows: PublicChannel[]): void {
  http.GET.mockResolvedValue({ data: { data: rows } });
}

beforeEach(() => {
  queryClient.clear();
  http.GET.mockReset();
  queryClient.setQueryData(['channels', 'mine'], [mine('c1', 'me')]);
  queryClient.setQueryData(['channels', 'moderated'], []);
});

describe('the shell follows the keys held in the named channel', () => {
  it('sends a person who holds no page to the viewer shell of an onboarded channel', async () => {
    directoryHas([listed('c2', 'x', true)]);
    queryClient.setQueryData(channelAccessKey('c2'), holding([]));
    const router = makeRouter();

    await router.push('/app/u/x/commands');

    expect(router.currentRoute.value.name).toBe('viewer-me');
    expect(router.currentRoute.value.params.channel).toBe('x');
  });

  it('keeps the operator page for a person who holds a management page', async () => {
    directoryHas([listed('c2', 'x', true)]);
    queryClient.setQueryData(channelAccessKey('c2'), holding(['commands:read']));
    const router = makeRouter();

    await router.push('/app/u/x/commands');

    expect(router.currentRoute.value.name).toBe('u:dashboard-commands');
  });

  it('counts the viewer chat as a viewer page, not a management page', async () => {
    directoryHas([listed('c2', 'x', true)]);
    queryClient.setQueryData(channelAccessKey('c2'), holding(['chat:viewer:read']));
    const router = makeRouter();

    await router.push('/app/u/x/commands');

    expect(router.currentRoute.value.name).toBe('viewer-me');
  });

  it('sends a channel the bot does not serve to its chat, found by Twitch id', async () => {
    directoryHas([listed('777', 'x', false)]);
    const router = makeRouter();

    await router.push('/app/u/x/commands');

    expect(router.currentRoute.value.name).toBe('viewer-external-chat');
    expect(router.currentRoute.value.query.id).toBe('777');
  });

  it('reads a channel of the person from their own list, with no directory call', async () => {
    queryClient.setQueryData(channelAccessKey('c1'), holding(['dashboard:read']));
    const router = makeRouter();

    await router.push('/app/u/me/commands');

    expect(http.GET).not.toHaveBeenCalled();
    expect(router.currentRoute.value.name).toBe('u:dashboard-commands');
  });

  it('leaves a channel that does not exist to the no-access screen', async () => {
    directoryHas([]);
    const router = makeRouter();

    await router.push('/app/u/ghost/commands');

    expect(router.currentRoute.value.name).toBe('u:dashboard-commands');
  });
});
