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
import { lookup } from '@/lib/array';

import { errorText, fieldErrorLines, refusalProblems } from '../errors';

const words = { 'errors.forbidden': 'No right.', 'errors.invalid': 'Bad input.' } as const satisfies Record<string, string>;
const t = (key: string): string => lookup(words, key) ?? key;

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

describe('refusalProblems', () => {
  it('lists the problems of a refusal that names several fields', () => {
    const problem = {
      reason: 'weight-out-of-range',
      field: 'followed',
      subject: '',
      value: 900,
      limit: 100,
    };
    const error = failed('raid-rules-save', 400, { data: { problems: [problem] } });

    expect(refusalProblems(error)).toEqual([problem]);
  });

  it('reads the single violation of a validator refusal as one problem whose reason is its key', () => {
    const error = failed('warn', 400, { data: { key: 'some.key', field: 'offenseWindowHours' } });

    expect(refusalProblems(error)).toEqual([{
      reason: 'some.key',
      field: 'offenseWindowHours',
      subject: '',
    }]);
  });

  it('reads a list of violations with their accepted range as problems whose reason is the key', () => {
    const error = failed('warn', 400, {
      data: {
        violations: [
          {
            key: 'spam.policy.error.outOfRange',
            field: 'accountAgeGateDays',
            minimum: 0,
            maximum: 365,
          },
          { key: 'spam.policy.error.maxWindowBelowWindow', field: 'maxWindowSeconds' },
        ],
      },
    });

    expect(refusalProblems(error)).toEqual([
      {
        reason: 'spam.policy.error.outOfRange',
        field: 'accountAgeGateDays',
        subject: '',
        minimum: 0,
        maximum: 365,
      },
      {
        reason: 'spam.policy.error.maxWindowBelowWindow',
        field: 'maxWindowSeconds',
        subject: '',
      },
    ]);
  });

  it('is empty for an error that names no field', () => {
    expect(refusalProblems(new Error('boom'))).toEqual([]);
    expect(refusalProblems(failed('warn', 500))).toEqual([]);
  });
});
