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

import { queryClient } from '@/api/queryClient';
import { dashboardRoutes } from '@/router/dashboard';

import {
  dashboardAdminChannelConsoleRoute,
  dashboardAdminTenantsRoute,
} from '../route';

// The route guard reads the platform keys of the signed-in person; this one holds every key.
vi.mock('@/composables/usePlatformCan', async (importOriginal) => ({
  ...(await importOriginal<Record<string, unknown>>()),
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

const page = { template: '<div />' };

// The two real admin routes with their real guards and redirects; only the lazy page loader is a stand-in,
// so a test does not depend on the page that the route opens.
function testRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        ...dashboardAdminTenantsRoute,
        path: '/app/admin/tenants',
        component: page,
      },
      {
        ...dashboardAdminChannelConsoleRoute,
        path: '/app/admin/channels/:channelId/:tab?',
        component: page,
      },
      {
        path: '/',
        name: 'dashboard-home',
        component: page,
      },
    ],
  });
}

beforeEach(() => {
  queryClient.setQueryData(['auth', 'me'], {
    id: 'u1',
    isAdmin: true,
    platformPermissions: ['tenant:read'],
  });
});

describe('the admin tenants route', () => {
  it('is the one list of people in the dashboard, at admin/tenants', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-tenants');

    expect(registered?.path).toBe('admin/tenants');
    expect(registered?.meta?.navMode).toBe('admin');
    expect(registered?.beforeEnter).toBeDefined();
  });

  it('leaves no Channels list and no Users list behind', () => {
    const names = (dashboardRoutes.children ?? []).map((route) => String(route.name));

    expect(names).not.toContain('dashboard-admin-channels');
    expect(names).not.toContain('dashboard-admin-users');
    expect(names).toContain('dashboard-admin-channels-console');
  });

  it('keeps the address of the channel console', () => {
    const children = dashboardRoutes.children ?? [];
    const console = children.find((route) => route.name === 'dashboard-admin-channels-console');

    expect(console?.path).toBe('admin/channels/:channelId/:tab?');
  });

  it('opens for an admin who holds the tenant read key, with its query kept', async () => {
    const router = testRouter();

    await router.push('/app/admin/tenants?search=stoney&kind=streamer');

    expect(router.currentRoute.value.name).toBe('dashboard-admin-tenants');
    expect(router.currentRoute.value.query).toEqual({ search: 'stoney', kind: 'streamer' });
  });

  it('sends a person who is not an admin to the home page', async () => {
    queryClient.setQueryData(['auth', 'me'], {
      id: 'u1',
      isAdmin: false,
      platformPermissions: [],
    });

    const router = testRouter();

    await router.push('/app/admin/tenants');

    expect(router.currentRoute.value.name).toBe('dashboard-home');
  });

  it('sends an old link to one tenant to the console of that channel', async () => {
    const router = testRouter();

    await router.push('/app/admin/tenants?tenant=t1');

    expect(router.currentRoute.value.name).toBe('dashboard-admin-channels-console');
    expect(router.currentRoute.value.params.channelId).toBe('t1');
    expect(router.currentRoute.value.fullPath).toBe('/app/admin/channels/t1');
  });

  it('keeps the other filters when it sends an old link to the console', async () => {
    const router = testRouter();

    await router.push('/app/admin/tenants?tenant=t1&search=stoney');

    expect(router.currentRoute.value.fullPath).toBe('/app/admin/channels/t1?search=stoney');
  });
});
