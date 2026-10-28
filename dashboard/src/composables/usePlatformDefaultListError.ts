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

import { computed, type ComputedRef } from 'vue';
import { useI18n } from 'vue-i18n';

import { ApiError } from '@/api/errors';

// The text under a platform-defaults section that failed to load: a refusal reads differently from a fault.
export function usePlatformDefaultListError(failure: () => Error | null): ComputedRef<string> {
  const { t } = useI18n();

  return computed<string>(() => {
    const error = failure();

    return error instanceof ApiError && error.status === 403
      ? t('dashboard.admin.platformDefaults.forbidden')
      : t('dashboard.admin.platformDefaults.loadError');
  });
}
