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

import type { Timer } from '@/api/timers';
import { targetToWire } from '@/lib/bindingTarget';

import { changeOf, defaultMinutes, draftOf, problemOf, timerLimits } from '../components/timerDraft';

const fine = {
  mode: 'simple' as const,
  name: 'Follow',
  messages: 'Hello',
  intervalMinutes: 30,
  minChatActivity: 0,
  fireOnce: false,
  speakWithTts: false,
  target: null,
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

const timerRow = (extra: Record<string, unknown>): Timer =>
  ({
    id: 't1',
    name: 'Follow',
    messages: ['Hello'],
    intervalMinutes: 30,
    minChatActivity: 0,
    fireOnce: false,
    isEnabled: true,
    speakWithTts: false,
    target: null,
    ...extra,
  }) as Timer;

describe('reply mode of a timer', () => {
  it('opens a timer without a target as a simple reply and one with a target as a pipeline', () => {
    expect(draftOf(timerRow({})).mode).toBe('simple');
    expect(draftOf(timerRow({ target: targetToWire({ kind: 'pipeline', id: 'p1' }) })).mode).toBe('full');
  });

  it('clears the target and keeps the lines when the timer goes back to a simple reply', () => {
    const row = timerRow({ messages: [], target: targetToWire({ kind: 'pipeline', id: 'p1' }) });

    expect(changeOf({ ...draftOf(row), mode: 'simple' }, row)).toEqual({ target: targetToWire(null) });
  });

  it('empties the lines and sends the target when a simple timer becomes a pipeline', () => {
    const row = timerRow({});
    const draft = {
      ...draftOf(row),
      mode: 'full' as const,
      target: { kind: 'pipeline' as const, id: 'p1' },
    };

    expect(changeOf(draft, row)).toEqual({
      messages: [],
      target: targetToWire({ kind: 'pipeline', id: 'p1' }),
    });
  });

  it('is not changed by opening a timer that has both lines and a target', () => {
    const row = timerRow({ target: targetToWire({ kind: 'pipeline', id: 'p1' }) });

    expect(changeOf(draftOf(row), row)).toEqual({});
  });
});
