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
      beforeEnter: undefined,
      component: stub,
      path: `/app/${child.path}`,
    }) as RouteRecordRaw,
);

const router = createRouter({ history: createMemoryHistory(), routes: children });

const entry = dashboardNav
  .flatMap((group) => group.entries)
  .find((candidate) => candidate.route === 'dashboard-voice-triggers');

describe('the voice triggers sidebar entry', () => {
  it('sits in the dashboard sidebar, guarded by the voice triggers read action', () => {
    expect(entry).toMatchObject({
      label: 'voiceTriggers',
      action: 'voicetriggers:read',
    });
  });

  it('opens the voice triggers page at its own address', async () => {
    await router.push({ name: entry?.route });

    expect(router.currentRoute.value.name).toBe('dashboard-voice-triggers');
    expect(router.currentRoute.value.path).toBe('/app/voice-triggers');
  });
});
