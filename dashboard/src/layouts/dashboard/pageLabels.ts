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

import type { RouteRecordName } from 'vue-router';

import { pageName } from '@/router/channelRoutes';

import { adminNav } from './adminNavigation';
import { dashboardNav } from './navigation';
import { settingsNav } from './settingsNavigation';

// A page that opens from a sidebar page but has no entry of its own is named after the page it opens from.
const childPages: ReadonlyMap<string, string> = new Map([
  ['dashboard-pipeline', 'dashboard-pipelines'],
  ['dashboard-admin-channels-console', 'dashboard-admin-tenants'],
]);

const labels: ReadonlyMap<string, string> = new Map(
  [dashboardNav, settingsNav, adminNav].flatMap((groups) =>
    groups.flatMap((group) =>
      group.entries.map((entry) =>
        [entry.route, entry.label] as const))),
);

// The words key (under frame.nav) of a page's plain name, or null for a page no sidebar lists.
export function pageLabel(routeName: RouteRecordName | null | undefined): string | null {
  const name = pageName(String(routeName ?? ''));

  return labels.get(childPages.get(name) ?? name) ?? null;
}
