// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
const quotesEntry = entries.find((entry) => entry.label === 'quotes');

describe('quotes sidebar entry', () => {
  it('exists in the dashboard sidebar', () => {
    expect(quotesEntry?.route).toBe('dashboard-quotes');
  });

  it('opens the quotes page', async () => {
    await router.push({ name: quotesEntry?.route });

    expect(router.currentRoute.value.name).toBe('dashboard-quotes');
    expect(router.currentRoute.value.path).toBe('/app/quotes');
  });
});
