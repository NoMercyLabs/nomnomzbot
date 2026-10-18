// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { toast } from 'vue-sonner';

import { ApiError } from '@/api/errors';
import { i18n } from '@/i18n';

// The bot answers 503 when VTube Studio cannot be reached. Its English reason is for logs; the screen shows a key.
function keyOf(error: Error): string {
  if (!(error instanceof ApiError))
    return 'errors.failed';

  return error.status === 503
    ? 'dashboard.vts.unavailable'
    : error.messageKey;
}

// A VTube Studio action was refused: the toast speaks the page's language, never the server's English.
export function toastVtsError(error: Error): void {
  const message = i18n.global.t(keyOf(error));

  toast.error(message);
}
