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

import { sameTarget, targetFromWire, targetToWire } from '@/lib/bindingTarget';

describe('an action target carries its parameters', () => {
  const picked = {
    kind: 'action',
    id: '',
    actionType: 'play_sound',
    params: { volume: 80 },
  } as const;

  it('sends the parameters to the server', () => {
    expect(targetToWire(picked)).toEqual({
      kind: 2,
      id: '',
      actionType: 'play_sound',
      params: { volume: 80 },
    });
  });

  it('reads the parameters back from the server', () => {
    const wire = targetToWire(picked);

    expect(targetFromWire(wire)).toEqual(picked);
  });

  it('leaves params out when an action has none', () => {
    const wire = targetToWire({
      kind: 'action',
      id: '',
      actionType: 'song_skip',
    });

    expect(wire).not.toHaveProperty('params');
  });

  it('tells two targets apart when only the parameters differ', () => {
    const louder = { ...picked, params: { volume: 100 } };

    expect(sameTarget(picked, { ...picked })).toBe(true);
    expect(sameTarget(picked, louder)).toBe(false);
  });
});
