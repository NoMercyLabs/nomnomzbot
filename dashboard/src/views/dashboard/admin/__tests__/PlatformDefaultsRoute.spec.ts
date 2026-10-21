// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
