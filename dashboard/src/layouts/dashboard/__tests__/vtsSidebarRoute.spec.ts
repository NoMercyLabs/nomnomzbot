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

import { describe, expect, it } from 'vitest';
import { createMemoryHistory, createRouter, type RouteRecordRaw } from 'vue-router';

import { dashboardRoutes } from '@/router/dashboard';

import { dashboardNav } from '../navigation';

// The real route table, without the guards, and with the real lazy loader of the VTube Studio page only.
const children: RouteRecordRaw[] = (dashboardRoutes.children ?? []).map(
  (child) =>
    ({
      ...child,
      beforeEnter: undefined,
      path: `/app/${child.path}`,
    }) as RouteRecordRaw,
);

const router = createRouter({ history: createMemoryHistory(), routes: children });

const sidebarRoutes = dashboardNav.flatMap((group) => group.entries.map((entry) => entry.route));

describe('VTube Studio sidebar entry', () => {
  it('is in the sidebar', () => {
    expect(sidebarRoutes).toContain('dashboard-vtube-studio');
  });

  it('opens the VTube Studio page at its own path', async () => {
    await router.push({ name: 'dashboard-vtube-studio' });

    const [record] = router.currentRoute.value.matched;
    const page = record?.components?.default as { __name?: string } | undefined;

    expect(router.currentRoute.value.path).toBe('/app/vtube-studio');
    expect(page?.__name).toBe('VtsView');
  });
});
