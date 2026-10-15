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

import { failed } from '@/api/errors';

import { errorText, fieldErrorLines } from '../errors';

const words: Record<string, string> = { 'errors.forbidden': 'No right.', 'errors.invalid': 'Bad input.' };
const t = (key: string): string => words[key] ?? key;

describe('errorText', () => {
  it('shows the translated message and the server detail of a typed error', () => {
    const error = failed('warn', 403, { detail: 'You are not a moderator.' });

    expect(errorText(error, t)).toBe('No right. You are not a moderator.');
  });

  it('shows only the translated message when the server gave no detail', () => {
    expect(errorText(failed('warn', 403), t)).toBe('No right.');
  });

  it('shows the message of a plain error', () => {
    expect(errorText(new Error('boom'), t)).toBe('boom');
  });
});

describe('fieldErrorLines', () => {
  it('lists every field message of a validation failure', () => {
    const error = failed('warn', 400, { errors: { Reason: ['Reason is required.'], Name: ['Too long.'] } });

    expect(fieldErrorLines(error)).toEqual(['Reason is required.', 'Too long.']);
  });

  it('is empty for an error without field errors', () => {
    expect(fieldErrorLines(new Error('boom'))).toEqual([]);
    expect(fieldErrorLines(failed('warn', 500))).toEqual([]);
  });
});
