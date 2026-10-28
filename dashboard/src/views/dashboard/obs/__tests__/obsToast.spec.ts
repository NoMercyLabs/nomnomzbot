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
