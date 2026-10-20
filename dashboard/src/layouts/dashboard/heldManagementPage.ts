// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { dashboardNav } from './navigation';

// The viewer chat is a page every viewer has, so it never makes someone an operator.
const viewerPage = 'dashboard-viewer-chat';

// True when the held action keys open at least one management page: the rule that picks the operator
// shell (/app/u/x) over the viewer shell (/me/x) in a channel. The shell follows the keys, never a role name.
export function heldManagementPage(held: ReadonlySet<string>): boolean {
  return dashboardNav
    .flatMap((group) => group.entries)
    .filter((entry) => entry.route !== viewerPage)
    .some((entry) => entry.action !== undefined && held.has(entry.action));
}
