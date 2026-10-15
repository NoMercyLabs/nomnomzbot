// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { adminNav } from './adminNavigation';
import { dashboardNav, type NavGroup, type NavMode } from './navigation';
import { settingsNav } from './settingsNavigation';
import { viewerNav } from './viewerNavigation';

// The groups each sidebar shows; the open page picks the mode (route meta navMode).
export const navByMode = {
  dashboard: dashboardNav,
  admin: adminNav,
  viewer: viewerNav,
  settings: settingsNav,
} as const satisfies Record<NavMode, readonly NavGroup[]>;
