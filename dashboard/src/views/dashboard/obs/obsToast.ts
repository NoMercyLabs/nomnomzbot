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

import { toast } from 'vue-sonner';

import { ApiError } from '@/api/errors';
import { i18n } from '@/i18n';

// The bot answers 503 when OBS cannot be reached. Its English reason is for logs; the screen shows a key.
function keyOf(error: Error): string {
  if (!(error instanceof ApiError))
    return 'errors.failed';

  return error.status === 503
    ? 'dashboard.obs.unavailable'
    : error.messageKey;
}

// An OBS action was refused: the toast speaks the page's language, never the server's English.
export function toastObsError(error: Error): void {
  const key = keyOf(error);

  toast.error(i18n.global.t(key));
}
