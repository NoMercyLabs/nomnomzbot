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

import { splitLinks, splitMatches } from '../text';

describe('splitMatches', () => {
  it('splits a text around every case-insensitive hit of the query', () => {
    expect(splitMatches('User.Name user', 'user')).toEqual([
      { text: 'User', match: true },
      { text: '.Name ', match: false },
      { text: 'user', match: true },
    ]);
  });

  it('returns the whole text unmarked for an empty query or no hit', () => {
    expect(splitMatches('abc', '')).toEqual([{ text: 'abc', match: false }]);
    expect(splitMatches('abc', 'x')).toEqual([{ text: 'abc', match: false }]);
  });

  it('treats regex characters in the query as plain text', () => {
    expect(splitMatches('a.<n>', '.<')).toEqual([
      { text: 'a', match: false },
      { text: '.<', match: true },
      { text: 'n>', match: false },
    ]);
  });
});

describe('splitLinks', () => {
  it('keeps the text before a link and leaves a trailing period out of the link', () => {
    expect(splitLinks('Open https://developer.spotify.com/dashboard.')).toEqual([
      { kind: 'text', value: 'Open ' },
      { kind: 'open', value: 'https://developer.spotify.com/dashboard' },
      { kind: 'text', value: '.' },
    ]);
  });

  it('finds two links in one line with the text between them', () => {
    expect(splitLinks('Go to http://a.example/x, then https://b.example/y; done')).toEqual([
      { kind: 'text', value: 'Go to ' },
      { kind: 'open', value: 'http://a.example/x' },
      { kind: 'text', value: ', then ' },
      { kind: 'open', value: 'https://b.example/y' },
      { kind: 'text', value: '; done' },
    ]);
  });

  it('leaves a closing bracket and a query string in the right place', () => {
    expect(splitLinks('(see https://a.example/p?x=1&y=2)')).toEqual([
      { kind: 'text', value: '(see ' },
      { kind: 'open', value: 'https://a.example/p?x=1&y=2' },
      { kind: 'text', value: ')' },
    ]);
  });

  it('marks an address under /api/ as one to paste and every other address as one to open', () => {
    expect(
      splitLinks('Set https://dev.nomnomz.bot/api/v1/auth/twitch/callback and see https://dev.twitch.tv/console/apps.'),
    ).toEqual([
      { kind: 'text', value: 'Set ' },
      { kind: 'paste', value: 'https://dev.nomnomz.bot/api/v1/auth/twitch/callback' },
      { kind: 'text', value: ' and see ' },
      { kind: 'open', value: 'https://dev.twitch.tv/console/apps' },
      { kind: 'text', value: '.' },
    ]);
  });

  it('returns one text part when the line has no link', () => {
    expect(splitLinks('Create an application')).toEqual([
      { kind: 'text', value: 'Create an application' },
    ]);
  });

  it('returns nothing for an empty line', () => {
    expect(splitLinks('')).toEqual([]);
  });
});
