// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { RouteRecordName } from 'vue-router';

import { pageName } from '@/router/channelRoutes';

import { adminNav } from './adminNavigation';
import { dashboardNav } from './navigation';
import { settingsNav } from './settingsNavigation';

// A page that opens from a sidebar page but has no entry of its own is named after the page it opens from.
const childPages: ReadonlyMap<string, string> = new Map([['dashboard-pipeline', 'dashboard-pipelines']]);

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
