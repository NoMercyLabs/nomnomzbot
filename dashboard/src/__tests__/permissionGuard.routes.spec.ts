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
import type { RouteRecordRaw } from 'vue-router';

import { adminOnly } from '@/views/dashboard/admin/route';

import { dashboardChannelRoutes, dashboardRoutes } from '../router/dashboard';

// Stoney 2026-10-18: "every tab and pannel and button and page needs to have proper permission checks before being shown".
// A page is guarded when it names a channel action key, opens itself on purpose, or is an admin page behind adminOnly.
function isGuarded(route: RouteRecordRaw): boolean {
  if (route.meta?.action !== undefined || route.meta?.open === true)
    return true;

  return route.meta?.navMode === 'admin' && route.beforeEnter === adminOnly;
}

function nameOf(route: RouteRecordRaw): string {
  return String(route.name);
}

describe('every page under /app carries a permission check', () => {
  const pages = dashboardRoutes.children ?? [];

  it('covers the real route table', () => {
    expect(pages.length).toBeGreaterThan(50);
  });

  it('names an action key, opens on purpose, or sits behind adminOnly', () => {
    const unguarded = pages.filter((route) => !isGuarded(route)).map(nameOf);

    expect(unguarded).toEqual([]);
  });

  it('keeps the same guard on the /app/u/<channel> twin of each page', () => {
    const twins = dashboardChannelRoutes.children ?? [];
    const unguarded = twins.filter((route) => !isGuarded(route)).map(nameOf);

    expect(twins.length).toBeGreaterThan(0);
    expect(unguarded).toEqual([]);
  });
});
