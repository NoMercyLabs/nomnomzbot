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
