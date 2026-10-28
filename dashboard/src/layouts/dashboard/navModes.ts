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

import { adminNav } from './adminNavigation';
import { dashboardNav, type NavGroup, type NavMode } from './navigation';
import { settingsNav } from './settingsNavigation';

// The groups each sidebar shows; the open page picks the mode (route meta navMode).
export const navByMode = {
  dashboard: dashboardNav,
  admin: adminNav,
  settings: settingsNav,
} as const satisfies Record<NavMode, readonly NavGroup[]>;
