// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
