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
