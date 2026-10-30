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
import type { RouteRecordRaw } from 'vue-router';

import { adminOnly } from '@/views/dashboard/admin/route';

import { dashboardChannelRoutes, dashboardRoutes } from '../router/dashboard';

// Stoney 2026-10-18: "every tab and pannel and button and page needs to have proper permission checks before being shown".
// A page is guarded when it names a channel action key, opens itself on purpose, or is an admin page behind adminOnly.
// A redirect shows nothing; the page it lands on carries the guard.
function isGuarded(route: RouteRecordRaw): boolean {
  if (route.redirect !== undefined)
    return true;

  if (route.meta?.action !== undefined || route.meta?.open === true)
    return true;

  // A guard list counts when adminOnly runs first (the tenants page adds a redirect for old ?tenant= links).
  const guards = Array.isArray(route.beforeEnter)
    ? route.beforeEnter
    : [route.beforeEnter];

  return route.meta?.navMode === 'admin' && guards[0] === adminOnly;
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
