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

import { defaultMinutes, problemOf, timerLimits } from '../components/timerDraft';

const fine = {
  name: 'Follow',
  messages: 'Hello',
  intervalMinutes: 30,
  minChatActivity: 0,
  fireOnce: false,
  speakWithTts: false,
};

describe('problemOf', () => {
  it('accepts a draft inside the bot limits', () => {
    expect(problemOf(fine)).toBeNull();
  });

  it('names the interval when it is below 1, above 1440, empty or not whole', () => {
    expect(problemOf({ ...fine, intervalMinutes: 0 })).toBe('interval');
    expect(problemOf({ ...fine, intervalMinutes: timerLimits.maxMinutes + 1 })).toBe('interval');
    expect(problemOf({ ...fine, intervalMinutes: Number.NaN })).toBe('interval');
    expect(problemOf({ ...fine, intervalMinutes: 2.5 })).toBe('interval');
  });

  it('names the chat lines when they are negative or above 10000', () => {
    expect(problemOf({ ...fine, minChatActivity: -1 })).toBe('activity');
    expect(problemOf({ ...fine, minChatActivity: timerLimits.maxActivity + 1 })).toBe('activity');
  });

  it('starts a new timer at the bot default of 30 minutes', () => {
    expect(defaultMinutes).toBe(30);
  });
});
