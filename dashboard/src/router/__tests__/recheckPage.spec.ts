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
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { currentUserQuery, type CurrentUser } from '@/api/auth';
import { queryClient } from '@/api/queryClient';
import { adminPageKeys } from '@/views/dashboard/admin/adminKeys';
import { adminOnly } from '@/views/dashboard/admin/route';

import { recheckOpenPage } from '../recheckPage';

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<unknown>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

// What the bot lets the person read now; a role change swaps this, like the bot's answer to the new header.
const held = vi.hoisted(() => ({ keys: ['dashboard:read'] as string[] }));

const page = defineComponent({ render: () => null });

function signedInAs(isAdmin: boolean): void {
  const user: CurrentUser = {
    id: 'u1',
    username: 'someone',
    displayName: 'Someone',
    broadcasterType: '',
    isAdmin,
    createdAt: '2026-01-01T00:00:00Z',
    platformPermissions: isAdmin
      ? Object.values(adminPageKeys)
      : [],
  };

  queryClient.setQueryData(currentUserQuery.queryKey, user);
}

function makeRouter(): Router {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        name: 'dashboard-home',
        path: '/home',
        component: page,
        meta: { action: 'dashboard:read' },
      },
      {
        name: 'dashboard-viewer-chat',
        path: '/viewer-chat',
        component: page,
        meta: { action: 'chat:viewer:read' },
      },
      {
        name: 'dashboard-admin-channels',
        path: '/admin/channels',
        component: page,
        meta: { navMode: 'admin' },
        beforeEnter: adminOnly,
      },
    ],
  });

  // Stands in for the access guard: a page whose action the person lost sends them to the viewer chat.
  router.beforeEach((to) => {
    const needed = to.meta.action;

    return needed === undefined || held.keys.includes(needed)
      ? true
      : { name: 'dashboard-viewer-chat' };
  });

  return router;
}

beforeEach(() => {
  held.keys = ['dashboard:read'];
  queryClient.clear();
});

describe('recheckOpenPage', () => {
  it('moves a person off an admin page once the bot no longer calls them an admin', async () => {
    signedInAs(true);
    const router = makeRouter();

    await router.push('/admin/channels');
    expect(router.currentRoute.value.name).toBe('dashboard-admin-channels');

    signedInAs(false);
    await recheckOpenPage(router);

    expect(router.currentRoute.value.name).toBe('dashboard-home');
  });

  it('runs the global page checks again on a page that stays the same route', async () => {
    signedInAs(false);
    const router = makeRouter();

    await router.push('/home');
    held.keys = ['chat:viewer:read'];
    await recheckOpenPage(router);

    expect(router.currentRoute.value.name).toBe('dashboard-viewer-chat');
  });

  it('keeps a page the person may still open, and keeps the history length', async () => {
    signedInAs(true);
    const router = makeRouter();

    await router.push('/admin/channels');
    const before = window.history.length;

    await recheckOpenPage(router);

    expect(router.currentRoute.value.name).toBe('dashboard-admin-channels');
    expect(window.history.length).toBe(before);
  });
});
