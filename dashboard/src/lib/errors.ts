// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { ApiError } from '@/api/errors';

type Translate = (key: string) => string;

// The text a screen shows for a failure: the translated message of a typed API error, followed by the
// reason the server gave when there is one. Any other error shows its own message.
// eslint-disable-next-line no-restricted-syntax -- takes whatever a query or a catch clause threw
export function errorText(error: unknown, t: Translate): string {
  if (error instanceof ApiError) {
    const base = t(error.messageKey);

    return error.detail === undefined
      ? base
      : `${base} ${error.detail}`;
  }

  return error instanceof Error
    ? error.message
    : String(error);
}

// The per-field messages of a validation failure, one line per message, for a form to show.
// eslint-disable-next-line no-restricted-syntax -- takes whatever a query or a catch clause threw
export function fieldErrorLines(error: unknown): string[] {
  if (!(error instanceof ApiError) || error.errors === undefined)
    return [];

  return Object.values(error.errors).flat();
}
