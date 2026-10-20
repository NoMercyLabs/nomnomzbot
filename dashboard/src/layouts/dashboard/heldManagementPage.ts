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

// The viewer chat and the store catalog (every viewer reads it to spend points) are pages every viewer
// has, so they never make someone an operator.
const viewerPages = new Set<string>(['dashboard-viewer-chat', 'dashboard-economy']);

// True when the held action keys open at least one management page: the rule that picks the operator
// shell (/app/u/x) over the viewer shell (/me/x) in a channel. The shell follows the keys, never a role name.
export function heldManagementPage(held: ReadonlySet<string>): boolean {
  return dashboardNav
    .flatMap((group) => group.entries)
    .filter((entry) => !viewerPages.has(entry.route))
    .some((entry) => entry.action !== undefined && held.has(entry.action));
}
