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

// One tab of a page's tab row. It lives here, not in PageTabs.vue, so lint can read the type.
export interface PageTab<View extends string = string> {
  value: View;
  label: string;
  count?: number;
  // A locked tab shows a lock and cannot open; the reason is its tooltip.
  disabled?: boolean;
  reason?: string;
}
