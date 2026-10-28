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

import { describe, expect, it } from 'vitest';

import { i18n } from '@/i18n';

import { dashboardAdminPlatformDefaultsRoute } from '../route';

describe('the platform defaults route', () => {
  it('brings the action names the page shows, not only its own words', async () => {
    await dashboardAdminPlatformDefaultsRoute.component();

    expect(i18n.global.te('dashboard.settings.permissions.action.widget_read')).toBe(true);
    expect(i18n.global.te('dashboard.settings.permissions.matrix.unnamed')).toBe(true);
  });
});
