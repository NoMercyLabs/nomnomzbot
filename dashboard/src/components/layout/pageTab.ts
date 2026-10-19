// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// One tab of a page's tab row. It lives here, not in PageTabs.vue, so lint can read the type.
export interface PageTab<View extends string = string> {
  value: View;
  label: string;
  count?: number;
  // A locked tab shows a lock and cannot open; the reason is its tooltip.
  disabled?: boolean;
  reason?: string;
}
