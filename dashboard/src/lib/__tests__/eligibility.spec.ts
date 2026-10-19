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

import { readSubOnly, writeSubOnly } from '../eligibility';

describe('media share eligibility rules', () => {
  it('reads no rule, empty text and broken text as open to everyone', () => {
    expect(readSubOnly(null)).toBe(false);
    expect(readSubOnly(undefined)).toBe(false);
    expect(readSubOnly('')).toBe(false);
    expect(readSubOnly('{nope')).toBe(false);
    expect(readSubOnly('[]')).toBe(false);
  });

  it('reads only a true subOnly as subscribers only', () => {
    expect(readSubOnly('{"subOnly":true}')).toBe(true);
    expect(readSubOnly('{"subOnly":false}')).toBe(false);
    expect(readSubOnly('{"subOnly":"true"}')).toBe(false);
  });

  it('writes subscribers only as the rule the bot enforces', () => {
    expect(writeSubOnly(null, true)).toBe('{"subOnly":true}');
  });

  it('writes open to everyone as empty text so the bot clears the rules', () => {
    expect(writeSubOnly('{"subOnly":true}', false)).toBe('');
    expect(writeSubOnly(null, false)).toBe('');
  });

  it('keeps rules this editor does not know when it flips subOnly', () => {
    expect(writeSubOnly('{"minAgeDays":3}', true)).toBe('{"minAgeDays":3,"subOnly":true}');
    expect(writeSubOnly('{"minAgeDays":3,"subOnly":true}', false)).toBe('{"minAgeDays":3}');
  });

  it('starts over when the stored text is broken', () => {
    expect(writeSubOnly('{nope', true)).toBe('{"subOnly":true}');
  });
});
