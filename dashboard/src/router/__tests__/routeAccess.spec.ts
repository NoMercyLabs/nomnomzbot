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
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { channelAccessKey, type ResolvedAccess } from '@/api/access';
import type { ChannelSummary } from '@/api/channels';
import { queryClient } from '@/api/queryClient';
import { chosenChannelId } from '@/composables/useCurrentChannel';

import { accessGuard } from '../accessGuard';
import { dashboardChannelRoutes, dashboardRoutes } from '../dashboard';

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<unknown>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

function realRouter(): Router {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [dashboardRoutes, dashboardChannelRoutes],
  });

  router.beforeEach(accessGuard(router));

  return router;
}

function channel(id: string, login: string): ChannelSummary {
  return {
    id,
    login,
    displayName: login,
    isLive: false,
    role: 'broadcaster',
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

beforeEach(() => {
  queryClient.clear();
  http.GET.mockReset();
  chosenChannelId.value = 'c1';
  queryClient.setQueryData(['channels', 'mine'], [channel('c1', 'me')]);
  queryClient.setQueryData(['channels', 'moderated'], []);
});

describe('the real dashboard routes', () => {
  it('keep a person who only holds chat:read out of commands and put them on a page they may open', async () => {
    queryClient.setQueryData(channelAccessKey('c1'), holding(['chat:read']));
    const router = realRouter();

    await router.push('/app/commands');

    expect(router.currentRoute.value.name).toBe('dashboard-chat');
    expect(router.currentRoute.value.meta.action).toBe('chat:read');
  });

  it('open commands for a person who holds commands:read', async () => {
    queryClient.setQueryData(channelAccessKey('c1'), holding(['commands:read']));
    const router = realRouter();

    await router.push('/app/commands');

    expect(router.currentRoute.value.name).toBe('dashboard-commands');
  });

  it('refuse the pipeline editor without the pipelines key and open it with the key', async () => {
    queryClient.setQueryData(channelAccessKey('c1'), holding(['dashboard:read']));
    const refused = realRouter();

    await refused.push('/app/pipelines/abc');

    expect(refused.currentRoute.value.name).toBe('dashboard-home');

    queryClient.setQueryData(channelAccessKey('c1'), holding(['pipelines:read']));
    const allowed = realRouter();

    await allowed.push('/app/pipelines/abc');

    expect(allowed.currentRoute.value.name).toBe('dashboard-pipeline');
  });
});
