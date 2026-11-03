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

import type { PipelineStep } from '@/api/pipelines';

import { moveStep } from '../components/stepEdits';
import { buildStepTree, keyboardSpot, locateStep } from '../components/stepTree';

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
    stop_on_match: false,
    ...over,
  };
}

// 1 say, 2 if (yes: 2.1 a, 2.2 b with child b1; no: 2.1 other), 3 wait.
const graph: PipelineStep[] = [
  step({ id: 'say' }),
  step({
    id: 'check',
    order: 1,
    block_kind: 'if',
    action: { type: 'if' },
  }),
  step({
    id: 'a',
    parent_step_id: 'check',
    branch: 'then',
  }),
  step({
    id: 'b',
    parent_step_id: 'check',
    branch: 'then',
    order: 1,
    block_kind: 'loop',
    action: { type: 'loop' },
  }),
  step({
    id: 'b1',
    parent_step_id: 'b',
  }),
  step({
    id: 'b2',
    parent_step_id: 'b',
    order: 1,
  }),
  step({
    id: 'other',
    parent_step_id: 'check',
    branch: 'else',
  }),
  step({
    id: 'wait',
    order: 2,
    action: { type: 'wait', seconds: 3 },
  }),
];

function shape(steps: PipelineStep[]): string[] {
  return steps
    .map((item) =>
      `${item.id}@${item.parent_step_id ?? '-'}/${item.branch ?? '-'}:${item.order}`)
    .sort();
}

function lane(steps: PipelineStep[], parentId: string | null, branch: string | null): string[] {
  return steps
    .filter((item) =>
      item.parent_step_id === parentId && item.branch === branch)
    .sort((left, right) =>
      left.order - right.order)
    .map((item) =>
      `${item.id}:${item.order}`);
}

describe('moveStep', () => {
  it('moves a step down inside its lane and numbers the lane again', () => {
    const moved = moveStep(graph, 'say', {
      parentId: null,
      branch: null,
      order: 1,
    });

    expect(lane(moved, null, null)).toEqual(['check:0', 'say:1', 'wait:2']);
  });

  it('moves a step up inside its lane', () => {
    const moved = moveStep(graph, 'wait', {
      parentId: null,
      branch: null,
      order: 0,
    });

    expect(lane(moved, null, null)).toEqual(['wait:0', 'say:1', 'check:2']);
  });

  it('moves a step into a branch, and the lane it left closes the gap', () => {
    const moved = moveStep(graph, 'say', {
      parentId: 'check',
      branch: 'else',
      order: 0,
    });

    expect(lane(moved, 'check', 'else')).toEqual(['say:0', 'other:1']);
    expect(lane(moved, null, null)).toEqual(['check:0', 'wait:1']);

    expect(moved.find((item) =>
      item.id === 'say')?.parent_step_id).toBe('check');
  });

  it('moves a step out of a branch to the top level', () => {
    const moved = moveStep(graph, 'a', {
      parentId: null,
      branch: null,
      order: 2,
    });

    expect(lane(moved, null, null)).toEqual(['say:0', 'check:1', 'a:2', 'wait:3']);
    expect(lane(moved, 'check', 'then')).toEqual(['b:0']);

    expect(moved.find((item) =>
      item.id === 'a')?.branch).toBeNull();
  });

  it('takes the whole subtree along and keeps its inner order', () => {
    const moved = moveStep(graph, 'b', {
      parentId: null,
      branch: null,
      order: 0,
    });

    expect(lane(moved, null, null)).toEqual(['b:0', 'say:1', 'check:2', 'wait:3']);
    expect(lane(moved, 'b', null)).toEqual(['b1:0', 'b2:1']);
    expect(lane(moved, 'check', 'then')).toEqual(['a:0']);
  });

  it('refuses a spot inside the step\'s own subtree', () => {
    const into = moveStep(graph, 'b', {
      parentId: 'b',
      branch: null,
      order: 0,
    });
    const deeper = moveStep(graph, 'check', {
      parentId: 'b1',
      branch: null,
      order: 0,
    });

    expect(into).toBe(graph);
    expect(deeper).toBe(graph);
  });

  it('changes nothing when the step goes to its own place', () => {
    const moved = moveStep(graph, 'check', {
      parentId: null,
      branch: null,
      order: 1,
    });

    expect(shape(moved)).toEqual(shape(graph));
  });

  it('leaves the given list as it was', () => {
    const before = JSON.stringify(graph);

    moveStep(graph, 'a', {
      parentId: null,
      branch: null,
      order: 0,
    });

    expect(JSON.stringify(graph)).toBe(before);
  });

  it('leaves the list alone for an unknown id', () => {
    const moved = moveStep(graph, 'nope', {
      parentId: null,
      branch: null,
      order: 0,
    });

    expect(moved).toBe(graph);
  });
});

describe('keyboardSpot', () => {
  const tree = buildStepTree(graph);

  it('moves one place down and up in the lane', () => {
    expect(keyboardSpot(tree, 'say', 'down')).toEqual({
      parentId: null,
      branch: null,
      order: 1,
    });

    expect(keyboardSpot(tree, 'wait', 'up')).toEqual({
      parentId: null,
      branch: null,
      order: 1,
    });
  });

  it('has no place past the ends of the lane', () => {
    expect(keyboardSpot(tree, 'say', 'up')).toBeNull();
    expect(keyboardSpot(tree, 'wait', 'down')).toBeNull();
  });

  it('takes a step out of its block to just after the block', () => {
    const spot = keyboardSpot(tree, 'a', 'left');

    expect(spot).toEqual({
      parentId: null,
      branch: null,
      order: 2,
    });

    expect(keyboardSpot(tree, 'say', 'left')).toBeNull();
  });

  it('puts a step into the block above it, at the end of its first lane', () => {
    expect(keyboardSpot(tree, 'wait', 'right')).toEqual({
      parentId: 'check',
      branch: 'then',
      order: 2,
    });
  });

  it('has no block to go into when nothing above takes children', () => {
    expect(keyboardSpot(tree, 'say', 'right')).toBeNull();
    expect(keyboardSpot(tree, 'b1', 'right')).toBeNull();
    expect(keyboardSpot(tree, 'b2', 'right')).toBeNull();
  });
});

describe('locateStep', () => {
  it('gives the place in its lane and the lane length', () => {
    const tree = buildStepTree(graph);

    expect(locateStep(tree, 'b')).toEqual({ position: 2, total: 2 });
    expect(locateStep(tree, 'wait')).toEqual({ position: 3, total: 3 });
    expect(locateStep(tree, 'nope')).toBeNull();
  });
});
