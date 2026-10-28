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

import { navByMode } from '@/layouts/dashboard/navModes';

import { dashboardRoutes } from '../dashboard';

const pages: RouteRecordRaw[] = dashboardRoutes.children ?? [];

// A back link repeats the home page on purpose; it is a way out of a mode, not a home.
function sidebarRouteNames(groups: (typeof navByMode)[keyof typeof navByMode]): string[] {
  return groups
    .flatMap((group) => group.entries)
    .filter((entry) => !('back' in entry))
    .map((entry) => entry.route);
}

function viewOf(route: RouteRecordRaw): string {
  return JSON.stringify([route.props ?? null, route.meta?.viewerChat ?? false]);
}

function repeatedNames(names: string[]): string[] {
  return names.filter((name, index) => names.indexOf(name) !== index);
}

describe('every page has one home in the navigation', () => {
  it('finds a page listed twice', () => {
    expect(repeatedNames(['a', 'b', 'a'])).toEqual(['a']);
    expect(repeatedNames(['a', 'b'])).toEqual([]);
  });

  it('lists each page once in each sidebar', () => {
    const repeated = Object.entries(navByMode).flatMap(([mode, groups]) =>
      repeatedNames(sidebarRouteNames(groups)).map((name) => `${mode}: ${name}`),
    );

    expect(repeated).toEqual([]);
  });

  // The viewer chat is the same page for a person without the operator chat; its entry shows only then.
  it('lets two routes share a component only when each opens its own view or role', () => {
    const clashes = pages
      .filter((route) =>
        pages.some((other) => {
          const sameComponent = other !== route && other.component === route.component;

          return sameComponent && viewOf(other) === viewOf(route);
        }),
      )
      .map((route) => String(route.name));

    expect(clashes).toEqual([]);
  });

  it('keeps Discord and the supporter sources in the settings only', () => {
    const moved = ['dashboard-settings-discord', 'dashboard-settings-supporter-sources'];
    const mainEntries = navByMode.dashboard
      .flatMap((group) => group.entries)
      .map((entry) => entry.route);
    const names = pages.map((route) => String(route.name));

    expect(mainEntries.filter((name) => moved.includes(name))).toEqual([]);
    expect(names.filter((name) => name === 'dashboard-discord')).toEqual([]);
    expect(names.filter((name) => name === 'dashboard-supporter-sources')).toEqual([]);
    expect(moved.every((name) => names.includes(name))).toBe(true);
  });
});
