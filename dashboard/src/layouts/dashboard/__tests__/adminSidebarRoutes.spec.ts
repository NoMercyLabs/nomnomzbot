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

import { adminNav } from '../adminNavigation';

const stub = defineComponent({ render: () => h('div') });

// The real route table without the admin guard and the lazy page loaders.
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

const sidebarRoutes = adminNav
  .filter((group) => group.key !== 'back')
  .flatMap((group) => group.entries.map((entry) => entry.route));

const isComingSoon = (route: RouteRecordRaw): boolean => Boolean(route.meta?.comingSoon);

const adminRoutes = children.filter((child) => child.meta?.navMode === 'admin');

// A page that opens from a sidebar page (its name is the entry's name plus a suffix) lights that entry; it has no entry of its own.
const isChildPage = (route: RouteRecordRaw): boolean =>
  sidebarRoutes.some((entry) => String(route.name).startsWith(`${entry}-`));

const builtAdminPages = adminRoutes.filter((route) => !isComingSoon(route) && !isChildPage(route));
const comingSoonNames = adminRoutes.filter(isComingSoon).map((route) => String(route.name));
const builtSidebarRoutes = sidebarRoutes.filter((name) => !comingSoonNames.includes(name));

describe('admin sidebar', () => {
  it('lists every sidebar route once', () => {
    expect(new Set(sidebarRoutes).size).toBe(sidebarRoutes.length);
  });

  it('has one sidebar entry for every built admin page', () => {
    const missing = builtAdminPages
      .map((page) => String(page.name))
      .filter((name) => !sidebarRoutes.includes(name));

    expect(missing).toEqual([]);
  });

  it('has an admin route for every sidebar entry', () => {
    const missing = sidebarRoutes.filter(
      (name) => !adminRoutes.some((route) => route.name === name),
    );

    expect(missing).toEqual([]);
  });

  it.each(builtSidebarRoutes)('the sidebar entry %s opens its own admin page', async (name) => {
    await router.push({ name });

    const page = builtAdminPages.find((route) => route.name === name);

    expect(router.currentRoute.value.name).toBe(name);
    expect(router.currentRoute.value.path).toBe(page?.path);
    expect(router.currentRoute.value.meta.navMode).toBe('admin');
    expect(router.currentRoute.value.meta.comingSoon).toBeUndefined();
  });
});
