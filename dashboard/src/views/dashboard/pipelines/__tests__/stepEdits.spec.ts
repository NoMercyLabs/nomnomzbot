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

import type { PipelineStep } from '@/api/pipelines';

import {
  duplicateStep,
  insertSteps,
  laneEnd,
  numberLanes,
  patchStep,
  removeStep,
  withIds,
} from '../components/stepEdits';

function step(over: Partial<PipelineStep>): PipelineStep {
  return {
    id: null,
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    order: 0,
    action: { type: 'send_message', message: 'hi' },
    condition: null,
    continue_on_error: false,
    ...over,
  };
}

// 1 say, 2 if (yes: 2.1 inner), 3 wait.
const graph: PipelineStep[] = [
  step({ id: 'say' }),
  step({
    id: 'check',
    order: 1,
    block_kind: 'if',
    action: { type: 'if' },
  }),
  step({
    id: 'inner',
    parent_step_id: 'check',
    branch: 'then',
  }),
  step({
    id: 'wait',
    order: 2,
    action: { type: 'wait', seconds: 3 },
  }),
];

function counter(): () => string {
  let next = 0;

  return () => {
    next += 1;

    return `new${next}`;
  };
}

const shape = (steps: PipelineStep[]): string[] =>
  steps.map((item: PipelineStep) =>
    `${item.id}@${item.parent_step_id ?? '-'}:${item.order}`);

describe('the step edits', () => {
  it('gives every step the bot saved without an id one of its own, and keeps the rest', () => {
    const steps = withIds([step({}), step({ id: 'kept', order: 1 })], counter());

    expect(steps.map((item: PipelineStep) => item.id)).toEqual(['new1', 'kept']);
  });

  it('numbers each lane in the order the engine runs it, the list position breaking a tie', () => {
    const steps = numberLanes([
      step({ id: 'a' }),
      step({ id: 'b' }),
      step({
        id: 'inner',
        parent_step_id: 'a',
        order: 5,
      }),
      step({ id: 'c' }),
    ]);

    expect(shape(steps)).toEqual(['a@-:0', 'b@-:1', 'inner@a:0', 'c@-:2']);

    const copied = duplicateStep(steps, 'a', counter());

    expect(shape(copied).slice(0, 4)).toEqual(['a@-:0', 'b@-:2', 'inner@a:0', 'c@-:3']);
  });

  it('removes a block with every step inside it and closes the gap in the order', () => {
    expect(shape(removeStep(graph, 'check'))).toEqual(['say@-:0', 'wait@-:1']);
  });

  it('duplicates a block with its inner steps right after it, under new ids', () => {
    const steps = duplicateStep(graph, 'check', counter());

    expect(shape(steps)).toEqual([
      'say@-:0',
      'check@-:1',
      'inner@check:0',
      'wait@-:3',
      'new1@-:2',
      'new2@new1:0',
    ]);

    expect(steps.find((item: PipelineStep) =>
      item.id === 'new2')?.branch).toBe('then');
  });

  it('adds a new step with its inner steps at the end of the lane it is given', () => {
    const block = [
      step({ id: 'pick', block_kind: 'random_branch' }),
      step({
        id: 'path',
        parent_step_id: 'pick',
        block_kind: 'random_case',
      }),
    ];
    const steps = insertSteps(graph, block, laneEnd(graph, 'check', 'then'));

    expect(shape(steps).slice(4)).toEqual(['pick@check:1', 'path@pick:0']);
    expect(steps[4]?.branch).toBe('then');
  });

  it('adds a new step at a place inside a lane and moves the later steps down', () => {
    const steps = insertSteps(graph, [step({ id: 'new' })], {
      parentId: null,
      branch: null,
      order: 1,
    });

    expect(shape(steps)).toEqual(['say@-:0', 'check@-:2', 'inner@check:0', 'wait@-:3', 'new@-:1']);
  });

  it('sets one setting of a step and keeps its other settings and the other steps', () => {
    const steps = patchStep(graph, 'wait', { action: { seconds: 5 }, continue_on_error: true });

    expect(steps[3]).toEqual({
      ...graph[3],
      action: { type: 'wait', seconds: 5 },
      continue_on_error: true,
    });

    expect(steps.slice(0, 3)).toEqual(graph.slice(0, 3));
  });

  it('drops a setting that is cleared, so the step saves without it', () => {
    const steps = patchStep(graph, 'say', { action: { message: undefined } });

    expect(steps[0]?.action).toEqual({ type: 'send_message' });
  });

  it('sets a block setting in its config, starting one when the block had none', () => {
    const steps = patchStep(graph, 'check', { block_config: { count: 3 } });

    expect(steps[1]?.block_config).toEqual({ count: 3 });
    expect(steps[1]?.action).toEqual({ type: 'if' });
  });

  it('leaves the saved list as it was', () => {
    removeStep(graph, 'say');
    duplicateStep(graph, 'wait', counter());
    insertSteps(graph, [step({ id: 'new' })], laneEnd(graph, null, null));
    patchStep(graph, 'wait', { action: { seconds: 9 }, block_config: { count: 1 } });

    expect(shape(graph)).toEqual(['say@-:0', 'check@-:1', 'inner@check:0', 'wait@-:2']);
  });
});
