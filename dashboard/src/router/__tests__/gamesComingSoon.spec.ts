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
