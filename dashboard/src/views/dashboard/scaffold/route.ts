// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { RouteMeta, RouteRecordRaw } from 'vue-router';

import { adminOnly } from '@/views/dashboard/admin/route';
import { navByMode } from '@/layouts/dashboard/navModes';
import type { NavEntry, NavMode } from '@/layouts/dashboard/navigation';

// A page reads its address from its name: dashboard-admin-audit-log is /app/admin/audit-log,
// dashboard-viewer-games is /app/me/games, and dashboard-obs is /app/obs.
function pathOf(name: string): string {
  const slug = name.replace('dashboard-', '');

  if (slug.startsWith('viewer-'))
    return slug.replace('viewer-', 'me/');

  return slug.replace(/^(admin|settings)-/, '$1/');
}

// A planned page asks for the action key of its sidebar entry; an entry without one is open to every signed-in person.
function access(entry: NavEntry): Pick<RouteMeta, 'action' | 'open'> {
  return entry.action === undefined
    ? { open: true }
    : { action: entry.action };
}

// A route for every page of every sidebar that has no built route yet; each opens the empty "coming soon" page.
export function scaffoldRoutes(built: ReadonlySet<string>): RouteRecordRaw[] {
  const modes = Object.keys(navByMode) as NavMode[];

  return modes.flatMap((mode: NavMode) =>
    navByMode[mode]
      .filter((group) => group.key !== 'back')
      .flatMap((group) => group.entries)
      .filter((entry) => !built.has(entry.route))
      .map((entry): RouteRecordRaw => ({
        path: pathOf(entry.route),
        name: entry.route,
        meta: {
          navMode: mode,
          comingSoon: entry.label,
          ...access(entry),
        },
        beforeEnter: mode === 'admin'
          ? adminOnly
          : undefined,
        component: () => import('./ComingSoonView.vue'),
      })));
}
