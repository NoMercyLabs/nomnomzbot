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

import { describe, expect, it } from 'vitest';

import { ApiError, failed, parseProblem, problemError, refused } from '../errors';

describe('parseProblem', () => {
  it('reads every ProblemDetails member of a 404 body', () => {
    const problem = parseProblem({
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
      title: 'Not Found',
      status: 404,
      detail: 'The channel does not exist.',
      instance: '/api/v1/channels/abc',
      traceId: '00-abc-def-00',
    });

    expect(problem).toEqual({
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
      title: 'Not Found',
      status: 404,
      detail: 'The channel does not exist.',
      instance: '/api/v1/channels/abc',
      traceId: '00-abc-def-00',
    });
  });

  it('reads the field errors of a ValidationProblemDetails 400 body', () => {
    const problem = parseProblem({
      title: 'One or more validation errors occurred.',
      status: '400',
      errors: { Reason: ['Reason is too long.', 'Reason has bad characters.'], Duration: ['Duration must be positive.'] },
    });

    expect(problem.status).toBe(400);

    expect(problem.errors).toEqual({
      Reason: ['Reason is too long.', 'Reason has bad characters.'],
      Duration: ['Duration must be positive.'],
    });
  });

  it('falls back to the bot message when there is no detail', () => {
    expect(parseProblem({ message: 'Custom code is switched off.' }).detail).toBe('Custom code is switched off.');
  });

  it('keeps the bot error code so a screen can name a setup reason in plain words', () => {
    const error = failed('discord roles', 500, {
      status: 'error',
      message: 'raw',
      code: 'DISCORD_BOT_TOKEN_NOT_CONFIGURED',
    });

    expect(error.serverCode).toBe('DISCORD_BOT_TOKEN_NOT_CONFIGURED');
    expect(error.code).toBe('discord/discord-roles');
  });

  it('reads the translatable key and the field of a validator refusal in data', () => {
    const problem = parseProblem({
      status: 400,
      message: 'The four weights must add up to 1.',
      code: 'VALIDATION_FAILED',
      data: { key: 'trust.policy.error.weightsMustSumToOne', field: 'requestCountWeight' },
    });

    expect(problem.violation).toEqual({
      key: 'trust.policy.error.weightsMustSumToOne',
      field: 'requestCountWeight',
    });

    expect(problem.detail).toBe('The four weights must add up to 1.');
  });

  it('ignores a data member that is not a key and a field', () => {
    expect(parseProblem({ data: { key: 'only.key' } }).violation).toBeUndefined();
    expect(parseProblem({ data: 'text' }).violation).toBeUndefined();
    expect(parseProblem({ data: { key: '', field: 'x' } }).violation).toBeUndefined();
  });

  it('gives an empty problem for a body that is not an object', () => {
    expect(parseProblem(undefined)).toEqual({});
    expect(parseProblem('nope')).toEqual({});
  });
});

describe('ApiError', () => {
  it('turns a 404 body into a typed error with code, origin, status and details', () => {
    const body = {
      title: 'Not Found',
      status: 404,
      detail: 'No such viewer.',
      traceId: 't-1',
    };
    const error = problemError('viewer profile', 404, body);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toBeInstanceOf(Error);
    expect(error.name).toBe('ApiError');
    expect(error.code).toBe('people/viewer-profile');
    expect(error.origin).toBe('people');
    expect(error.operation).toBe('viewer profile');
    expect(error.status).toBe(404);
    expect(error.messageKey).toBe('errors.notFound');
    expect(error.title).toBe('Not Found');
    expect(error.detail).toBe('No such viewer.');
    expect(error.traceId).toBe('t-1');
    expect(error.message).toBe('No such viewer.');
  });

  it('turns a 400 validation body into a typed error that keeps the field errors', () => {
    const body = { status: 400, errors: { Reason: ['Reason is required.'] } };
    const error = problemError('warn', 400, body);

    expect(error.code).toBe('people/warn');
    expect(error.messageKey).toBe('errors.invalid');
    expect(error.errors).toEqual({ Reason: ['Reason is required.'] });
  });

  it('keeps the validator violation on the error so a screen can mark the field', () => {
    const body = {
      status: 400,
      message: 'Decay is out of range.',
      data: { key: 'trust.policy.error.decayOutOfRange', field: 'accountAgeDecay' },
    };
    const error = problemError('save trust policy', 400, body);

    expect(error.violation).toEqual({
      key: 'trust.policy.error.decayOutOfRange',
      field: 'accountAgeDecay',
    });

    expect(error.messageKey).toBe('errors.invalid');
  });

  it('picks the message key from the status class', () => {
    expect(failed('stream', 401).messageKey).toBe('errors.forbidden');
    expect(failed('stream', 403).messageKey).toBe('errors.forbidden');
    expect(failed('stream', 429).messageKey).toBe('errors.rateLimited');
    expect(failed('stream', 503).messageKey).toBe('errors.server');
    expect(failed('stream', 418).messageKey).toBe('errors.failed');
  });

  it('keeps the ProblemDetails body that comes with a failed response', () => {
    const body = {
      title: 'Forbidden',
      detail: 'You are not a moderator.',
      traceId: 'abc-1',
    };
    const error = failed('shield', 403, body);

    expect(error.title).toBe('Forbidden');
    expect(error.detail).toBe('You are not a moderator.');
    expect(error.traceId).toBe('abc-1');
    expect(error.message).toBe('You are not a moderator.');
  });

  it('names the code and status when the body has no detail', () => {
    expect(failed('music queue', 500).message).toBe('music queue 500');
  });

  it('keeps the bot reason of a refusal that came with a 200', () => {
    const error = refused('timeout refused', 'That person is a moderator.');

    expect(error.status).toBeUndefined();
    expect(error.messageKey).toBe('errors.refused');
    expect(error.detail).toBe('That person is a moderator.');
  });
});
