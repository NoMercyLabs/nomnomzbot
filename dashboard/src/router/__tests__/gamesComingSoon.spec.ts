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

import { dashboardRoutes } from '../dashboard';

describe('the Games page', () => {
  const games = (dashboardRoutes.children ?? []).find((route) => route.name === 'dashboard-games');

  it('is served by the coming-soon page, not by a built games view', () => {
    expect(games?.meta?.comingSoon).toBe('games');
  });

  it('keeps the address and the sidebar action key', () => {
    expect(games?.path).toBe('games');
    expect(games?.meta?.action).toBe('games:session:read');
  });
});
