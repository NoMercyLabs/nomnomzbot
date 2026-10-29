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

import { describe, expect, it, vi } from 'vitest';
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router';
import { defineComponent } from 'vue';

import { signInGuard } from '@/router/guard';
import { siteRoutes } from '@/router/site';

vi.mock('@/api/auth', () => ({
  restoreSession: () => Promise.resolve(true),
  takeOAuthLanding: () => false,
}));

const page = defineComponent({ render: () => null });
const app: RouteRecordRaw = {
  path: '/app',
  name: 'app',
  component: page,
};

async function visit(path: string): Promise<string> {
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [siteRoutes, app],
  });

  router.beforeEach(signInGuard);
  await router.push(path);

  return router.currentRoute.value.path;
}

describe('a signed-in person on the public site', () => {
  it('can open the home page and stays on it', async () => {
    expect(await visit('/')).toBe('/');
  });

  it('is still sent past the sign-in page to the dashboard', async () => {
    expect(await visit('/login')).toBe('/app');
  });
});
