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

import { dashboardCatalogRoute } from '../route';

// The page recipes tab draws a real AdminCollectionPage, so it prints the admin words. Without them the pager
// buttons print their raw keys, 475 px wide on a phone, and widen the whole page.
describe('catalog route words', () => {
  it('loads the admin pager and crumb words the page recipes tab prints', async () => {
    await dashboardCatalogRoute.component();

    const t = i18n.global.t;

    expect(t('dashboard.admin.pager.previous')).toBe('Previous');
    expect(t('dashboard.admin.pager.next')).toBe('Next');
    expect(t('dashboard.admin.crumb')).toBe('Admin');
  });
});
