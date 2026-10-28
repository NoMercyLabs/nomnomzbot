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
import { defineComponent, h } from 'vue';
import { createMemoryHistory, createRouter, type RouteRecordRaw } from 'vue-router';

import { dashboardRoutes } from '@/router/dashboard';

import { dashboardNav } from '../navigation';

const stub = defineComponent({ render: () => h('div') });

// The real route table without the lazy page loaders.
const children: RouteRecordRaw[] = (dashboardRoutes.children ?? []).map(
  (child) =>
    ({
      ...child,
      component: stub,
      path: `/app/${child.path}`,
    }) as RouteRecordRaw,
);

const router = createRouter({ history: createMemoryHistory(), routes: children });

const entries = dashboardNav.flatMap((group) => group.entries);
const giveawaysEntry = entries.find((entry) => entry.label === 'giveaways');

describe('giveaways sidebar entry', () => {
  it('exists in the dashboard sidebar', () => {
    expect(giveawaysEntry?.route).toBe('dashboard-giveaways');
  });

  it('opens the giveaways page', async () => {
    await router.push({ name: giveawaysEntry?.route });

    expect(router.currentRoute.value.name).toBe('dashboard-giveaways');
    expect(router.currentRoute.value.path).toBe('/app/giveaways');
  });
});
