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

import type { PipelineTestRun } from '@/api/pipelines';

import { runMarks, runOf, runSummary } from '../components/stepRun';

const run = {
  trace: [
    {
      stepId: 'a',
      stepType: 'loop',
      iterations: 1,
      variableChanges: [],
    },
    {
      stepId: 'a',
      stepType: 'loop',
      iterations: 3,
      variableChanges: [],
    },
    {
      stepId: 'b',
      stepType: 'set_variable',
      variableChanges: [{
        key: 'x',
        before: null,
        after: '2',
      }],
      error: 'Too big.',
    },
  ],
} as unknown as PipelineTestRun;

describe('test run marks', () => {
  it('keeps the last pass of a step, and calls a step that is missing skipped', () => {
    const marks = runMarks(run);

    expect(runOf(marks, 'a').state).toBe('done');
    expect(runOf(marks, 'a').iterations).toBe(3);
    expect(runOf(marks, 'b').state).toBe('failed');
    expect(runOf(marks, 'b').error).toBe('Too big.');

    expect(runOf(marks, 'b').changes).toEqual([{
      key: 'x',
      before: null,
      after: '2',
    }]);

    expect(runOf(marks, 'c').state).toBe('skipped');
    expect(runOf(marks, null).state).toBe('skipped');
  });

  it('counts the steps that ran and the ones that failed', () => {
    expect(runSummary(run)).toEqual({ ran: 2, failed: 1 });
  });
});
