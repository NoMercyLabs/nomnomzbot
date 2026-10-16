// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiError } from '@/api/errors';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import nl from '../i18n/nl.json';
import { toastObsError } from '../obsToast';

const shown: string[] = [];

vi.mock('vue-sonner', () => ({
  toast: { error: (text: string): number => shown.push(text) },
}));

beforeEach(() => {
  shown.length = 0;
  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('nl', nl);
});

describe('toastObsError', () => {
  const bridgeOffline = new ApiError('obs state', 503, {
    detail: 'No OBS bridge is connected for this channel.',
  });

  it('shows the translated reason, never the server English, when OBS cannot be reached', () => {
    i18n.global.locale.value = 'nl';
    toastObsError(bridgeOffline);

    expect(shown).toEqual([nl.dashboard.obs.unavailable]);
    expect(shown[0]).not.toContain('bridge is connected');
  });

  it('shows the generic translated failure for an error that is not from the API', () => {
    i18n.global.locale.value = 'en';
    toastObsError(new Error('boom'));

    expect(shown).toEqual([i18n.global.t('errors.failed')]);
  });
});
