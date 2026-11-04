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

import { sentHistoryLimit, useSentHistory } from '../useSentHistory';

describe('useSentHistory', () => {
  it('walks back from the newest sent message and forward to the draft', () => {
    const history = useSentHistory();

    history.record('first');
    history.record('second');

    expect(history.older('my draft')).toBe('second');
    expect(history.older('second')).toBe('first');
    expect(history.older('first')).toBeNull();
    expect(history.newer()).toBe('second');
    expect(history.newer()).toBe('my draft');
    expect(history.newer()).toBeNull();
  });

  it('has nothing to show before a message is sent', () => {
    const history = useSentHistory();

    expect(history.older('')).toBeNull();
    expect(history.newer()).toBeNull();
  });

  it('keeps one copy of a repeated message and starts over after a send', () => {
    const history = useSentHistory();

    history.record('hi');
    history.record('hi');
    history.older('');
    history.record('bye');

    expect(history.older('')).toBe('bye');
    expect(history.older('bye')).toBe('hi');
    expect(history.older('hi')).toBeNull();
  });

  it('keeps only the newest messages up to the limit', () => {
    const history = useSentHistory();

    for (let i = 0; i <= sentHistoryLimit; i++) {
      history.record(`m${i}`);
    }

    let oldest = '';
    let step = history.older('');

    while (step !== null) {
      oldest = step;
      step = history.older(step);
    }

    expect(oldest).toBe('m1');
  });
});
