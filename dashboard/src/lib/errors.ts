// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { ApiError, type ApiFieldProblem } from '@/api/errors';

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

// Every broken field a refusal names, as one list. A refusal with one violation (its key is the reason) and a
// refusal with a list of problems read the same way, so a screen translates the reason and shows the line next
// to the field. Empty when the error names no field.
// eslint-disable-next-line no-restricted-syntax -- takes whatever a query or a catch clause threw
export function refusalProblems(error: unknown): ApiFieldProblem[] {
  if (!(error instanceof ApiError))
    return [];

  if (error.problems !== undefined)
    return error.problems;

  return error.violation === undefined
    ? []
    : [{
        reason: error.violation.key,
        field: error.violation.field,
        subject: '',
      }];
}
