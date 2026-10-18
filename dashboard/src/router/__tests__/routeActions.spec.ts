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

import { navByMode } from '@/layouts/dashboard/navModes';
import type { NavEntry } from '@/layouts/dashboard/navigation';

import { dashboardRoutes } from '../dashboard';

const checkedByOtherWork = ['admin', 'settings'];

function gatedRoutes(): RouteRecordRaw[] {
  const pages = dashboardRoutes.children ?? [];

  return pages.filter((route) => !checkedByOtherWork.includes(route.meta?.navMode ?? ''));
}

function sidebarEntries(): ReadonlyMap<string, NavEntry> {
  const groups = Object.values(navByMode).flat();
  const entries = groups.flatMap((group) => group.entries);

  return new Map(entries.map((entry) => [entry.route, entry]));
}

function nameOf(route: RouteRecordRaw): string {
  return String(route.name);
}

describe('the pages under /app', () => {
  it('each name an action key or open themselves on purpose', () => {
    const ungated = gatedRoutes().filter(
      (route) => route.meta?.action === undefined && route.meta?.open !== true,
    );

    expect(ungated.map(nameOf)).toEqual([]);
  });

  it('use the action key of their sidebar entry', () => {
    const entries = sidebarEntries();
    const differing = gatedRoutes()
      .filter((route) => route.meta?.open !== true)
      .filter((route) => {
        const entry = entries.get(nameOf(route));

        return entry?.action !== undefined && entry.action !== route.meta?.action;
      })
      .map((route) => {
        const sidebar = entries.get(nameOf(route))?.action;

        return `${nameOf(route)}: ${String(route.meta?.action)} vs ${String(sidebar)}`;
      });

    expect(differing).toEqual([]);
  });

  it('never open a page whose sidebar entry names an action', () => {
    const entries = sidebarEntries();
    const wrong = gatedRoutes()
      .filter((route) => route.meta?.open === true)
      .filter((route) => entries.get(nameOf(route))?.action !== undefined)
      .map(nameOf);

    expect(wrong).toEqual([]);
  });
});
