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
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router';

import { currentUserQuery, type CurrentUser } from '@/api/auth';
import { queryClient } from '@/api/queryClient';
import { adminNav } from '@/layouts/dashboard/adminNavigation';
import { adminPageKeys } from '@/views/dashboard/admin/adminKeys';
import { adminOnly } from '@/views/dashboard/admin/route';

import { accessGuard } from '../accessGuard';
import { platformPathGuard } from '../channelRoutes';
import { dashboardChannelRoutes, dashboardRoutes } from '../dashboard';

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<unknown>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

const channelLogin = 'streamer';
const homePage = 'dashboard-home';

function realRouter(): Router {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [dashboardRoutes, dashboardChannelRoutes],
  });

  router.beforeEach(platformPathGuard);
  router.beforeEach(accessGuard(router));

  return router;
}

const everyPageKey = Object.values(adminPageKeys);

// An admin by default holds every key an admin page needs; a test hands in fewer to take one away.
function signedInAs(isAdmin: boolean, platformPermissions: string[] = isAdmin
  ? everyPageKey
  : []): void {
  const user: CurrentUser = {
    id: 'u1',
    username: 'someone',
    displayName: 'Someone',
    broadcasterType: '',
    isAdmin,
    createdAt: '2026-01-01T00:00:00Z',
    platformPermissions,
  };

  queryClient.setQueryData(currentUserQuery.queryKey, user);
}

function adminPages(): RouteRecordRaw[] {
  const pages = dashboardRoutes.children ?? [];

  return pages.filter((route) => route.meta?.navMode === 'admin');
}

function adminPaths(): string[] {
  return adminPages().map((route) => `/app/${route.path}`);
}

function sidebarAdminPages(): string[] {
  return adminNav
    .filter((group) => group.key !== 'back')
    .flatMap((group) => group.entries)
    .map((entry) => entry.route);
}

beforeEach(() => {
  queryClient.clear();
  http.GET.mockReset();
});

describe('the platform admin route table', () => {
  it('lists every admin page of the sidebar as an admin route', () => {
    const named = adminPages().map((route) => String(route.name));

    expect(sidebarAdminPages().filter((name) => !named.includes(name))).toEqual([]);
  });

  it('puts the admin check on every route of the admin sidebar', () => {
    const unchecked = adminPages()
      .filter((route) => route.beforeEnter !== adminOnly)
      .map((route) => String(route.name));

    expect(unchecked).toEqual([]);
  });

  it('keeps every address under /app/admin inside the admin sidebar mode', () => {
    const pages = dashboardRoutes.children ?? [];
    const strays = pages
      .filter((route) => route.path.startsWith('admin/'))
      .filter((route) => route.meta?.navMode !== 'admin')
      .map((route) => String(route.name));

    expect(strays).toEqual([]);
  });

  it('has no channel twin of an admin page', () => {
    const twins = (dashboardChannelRoutes.children ?? []).filter(
      (route) => route.meta?.navMode === 'admin',
    );

    expect(twins).toEqual([]);
  });
});

describe('a person who is not a platform admin', () => {
  it('is sent home from every admin page', async () => {
    signedInAs(false);
    const paths = adminPaths();
    const landed: string[] = [];

    for (const path of paths) {
      const router = realRouter();

      await router.push(path);
      landed.push(String(router.currentRoute.value.name));
    }

    expect(paths.length).toBeGreaterThan(0);
    expect(landed).toEqual(paths.map(() => homePage));
  });

  it('is sent home from every old admin address typed under a named channel', async () => {
    signedInAs(false);
    const paths = adminPaths().map((path) => path.replace('/app/', `/app/u/${channelLogin}/`));
    const landed: string[] = [];

    for (const path of paths) {
      const router = realRouter();

      await router.push(path);
      landed.push(String(router.currentRoute.value.name));
    }

    expect(landed).toEqual(paths.map(() => homePage));
  });

  it('is sent home when the signed-in person cannot be read', async () => {
    queryClient.setQueryData(currentUserQuery.queryKey, null);
    const router = realRouter();

    await router.push('/app/admin/users');

    expect(router.currentRoute.value.name).toBe(homePage);
  });
});

describe('a platform admin who lacks the key of a page', () => {
  it('has a key in the table for every admin page', () => {
    const named = Object.keys(adminPageKeys);
    const unkeyed = adminPages()
      .map((route) => String(route.name))
      .filter((name) => !named.includes(name));

    expect(unkeyed).toEqual([]);
  });

  it('is sent home from that page and still opens the others', async () => {
    signedInAs(true, everyPageKey.filter((key) => key !== adminPageKeys['dashboard-admin-billing']));
    const billing = realRouter();
    const users = realRouter();

    await billing.push('/app/admin/billing');
    await users.push('/app/admin/users');

    expect(billing.currentRoute.value.name).toBe(homePage);
    expect(users.currentRoute.value.name).toBe('dashboard-admin-users');
  });

  it('is sent home when the bot sent no platform keys at all', async () => {
    signedInAs(true, []);
    const router = realRouter();

    await router.push('/app/admin/users');

    expect(router.currentRoute.value.name).toBe(homePage);
  });
});

describe('a platform admin', () => {
  it('opens every admin page', async () => {
    signedInAs(true);
    const pages = adminPages();
    const landed: string[] = [];

    for (const page of pages) {
      const router = realRouter();

      await router.push(`/app/${page.path}`);
      landed.push(String(router.currentRoute.value.name));
    }

    expect(landed).toEqual(pages.map((page) => String(page.name)));
  });
});
