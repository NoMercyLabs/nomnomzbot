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

import { dashboardPollsRoute } from '../route';

describe('polls route words', () => {
  it('loads the words the Start a poll and Prediction dialogs print', async () => {
    await dashboardPollsRoute.component();

    const t = i18n.global.t;

    expect(t('dashboard.home.quick.pollStart')).toBe('Start a poll');
    expect(t('dashboard.home.quick.pollAddChoice')).toBe('Add choice');
    expect(t('dashboard.home.quick.pollPoints')).toBe('Let viewers vote with channel points');
    expect(t('dashboard.home.quick.pollStart')).not.toContain('dashboard.home.quick.');
  });
});
