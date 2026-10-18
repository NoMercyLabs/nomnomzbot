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
import { queryClient } from '@/api/queryClient';
import { chosenChannelId } from '@/composables/useCurrentChannel';
import { accessGuard } from '@/router/accessGuard';

import { dashboardSettingsRoutes } from '../route';

vi.mock('@/api/client', () => ({ client: { GET: vi.fn(), use: (): undefined => undefined } }));

const page = defineComponent({ render: () => null });

// The real settings records, with the lazy page swapped for an empty one: only the gate is under test.
const gated = dashboardSettingsRoutes
  .filter((route) => typeof route.meta?.action === 'string')
  .map((route): RouteRecordRaw => ({ ...route, component: page }) as RouteRecordRaw);

const homeRoute = {
  path: '',
  name: 'dashboard-home',
  component: page,
  meta: { action: 'dashboard:read' },
} as const satisfies RouteRecordRaw;

function makeRouter(): Router {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app',
        component: page,
        children: [homeRoute, ...gated],
      },
    ],
  });

  router.beforeEach(accessGuard(router));

  return router;
}

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

beforeEach(() => {
  queryClient.clear();
  chosenChannelId.value = 'c1';

  queryClient.setQueryData(['channels', 'mine'], [
    {
      id: 'c1',
      login: 'me',
      displayName: 'me',
      isLive: false,
      role: 'broadcaster',
      provider: 'twitch',
    },
  ]);

  queryClient.setQueryData(['channels', 'moderated'], []);
});

describe('settings pages', () => {
  it('name an action key on every page that reads channel data', () => {
    expect(gated.length).toBeGreaterThanOrEqual(10);
  });

  // The bot basics and personality pages only read what the dashboard home reads, so they have no key of their own.
  it.each(
    gated
      .filter((route) => route.meta?.action !== 'dashboard:read')
      .map((route) => [route.name, route.path, route.meta?.action] as const),
  )(
    '%s sends a person without its key to the dashboard home',
    async (_name, path) => {
      hold(['dashboard:read']);
      const router = makeRouter();

      await router.push(`/app/${path}`);

      expect(router.currentRoute.value.name).toBe('dashboard-home');
    },
  );

  it.each(gated.map((route) => [route.name, route.path, route.meta?.action] as const))(
    '%s opens for a person who holds its key',
    async (name, path, action) => {
      hold(['dashboard:read', String(action)]);
      const router = makeRouter();

      await router.push(`/app/${path}`);

      expect(router.currentRoute.value.name).toBe(name);
    },
  );
});
