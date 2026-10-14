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
