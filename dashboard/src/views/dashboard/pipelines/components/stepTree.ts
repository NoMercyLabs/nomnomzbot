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

// The flat step list the bot stores, read as the tree the engine walks (PipelineEngine.ExecuteBlockAsync):
// if and try split their children by branch then/else, switch and random_branch hold one case step per
// lane, and loop and detached_step run all their children as one lane.
import type { BlockKind, PipelineStep } from '@/api/pipelines';

import type { Spot } from './stepEdits';

export type LaneKind = 'yes' | 'no' | 'works' | 'fails' | 'each' | 'case' | 'path' | 'alone';

export type StepLane = {
  kind: LaneKind;
  // The case step of a switch or random lane: it carries the match or the weight.
  head: PipelineStep | null;
  nodes: StepNode[];
  // Where a step added to this lane goes: under the block or its case step, on the block's branch.
  parentId: string | null;
  branch: string | null;
};

export type StepNode = {
  step: PipelineStep;
  // "3" at the top, "3.1" inside a lane of step 3.
  number: string;
  // Unique within the tree, unlike the number (every switch case has a "3.1") and the step id (the bot saves a
  // plain step without one): "3:0.1" is the first step in lane 0 of step 3.
  key: string;
  lanes: StepLane[];
};

type Place = { number: string; key: string };

type Siblings = Map<string | null, PipelineStep[]>;

const BRANCH_LANES = {
  if: ['yes', 'no'],
  try: ['works', 'fails'],
} as const satisfies Partial<Record<BlockKind, [LaneKind, LaneKind]>>;

const BRANCH_OF = {
  yes: 'then',
  no: 'else',
  works: 'then',
  fails: 'else',
} as const;

type BranchLane = LaneKind & keyof typeof BRANCH_OF;

function byParent(steps: PipelineStep[]): Siblings {
  const groups = new Map();

  for (const step of steps) {
    const list = groups.get(step.parent_step_id) ?? [];

    list.push(step);
    groups.set(step.parent_step_id, list);
  }

  for (const list of groups.values()) {
    list.sort((left: PipelineStep, right: PipelineStep) =>
      left.order - right.order);
  }

  return groups;
}

function childrenOf(step: PipelineStep, groups: Siblings): PipelineStep[] {
  return step.id === null
    ? []
    : (groups.get(step.id) ?? []);
}

// The prefix the steps of one lane of a node continue from.
function laneStart(place: Place, lane: number) {
  return { number: `${place.number}.`, key: `${place.key}:${lane}.` } as const satisfies Place;
}

function nodesOf(steps: PipelineStep[], groups: Siblings, start: Place): StepNode[] {
  return steps.map((step, index) =>
    nodeOf(step, groups, { number: `${start.number}${index + 1}`, key: `${start.key}${index + 1}` }));
}

function lanesOf(step: PipelineStep, groups: Siblings, place: Place): StepLane[] {
  const children = childrenOf(step, groups);

  if (step.block_kind === 'if' || step.block_kind === 'try') {
    return BRANCH_LANES[step.block_kind].map((kind: BranchLane, lane: number) => {
      const inLane = children.filter((child) =>
        child.branch === BRANCH_OF[kind]);

      return {
        kind,
        head: null,
        nodes: nodesOf(inLane, groups, laneStart(place, lane)),
        parentId: step.id,
        branch: BRANCH_OF[kind],
      };
    });
  }

  if (step.block_kind === 'switch' || step.block_kind === 'random_branch') {
    const kind = step.block_kind === 'switch'
      ? 'case'
      : 'path';

    return children.map((head, lane) => ({
      kind,
      head,
      nodes: nodesOf(childrenOf(head, groups), groups, laneStart(place, lane)),
      parentId: head.id,
      branch: null,
    }));
  }

  if (step.block_kind === 'loop' || step.block_kind === 'detached_step') {
    const kind = step.block_kind === 'loop'
      ? 'each'
      : 'alone';

    return [{
      kind,
      head: null,
      nodes: nodesOf(children, groups, laneStart(place, 0)),
      parentId: step.id,
      branch: null,
    }];
  }

  return [];
}

function nodeOf(step: PipelineStep, groups: Siblings, place: Place) {
  return {
    step,
    ...place,
    lanes: lanesOf(step, groups, place),
  } as const satisfies StepNode;
}

export function buildStepTree(steps: PipelineStep[]): StepNode[] {
  const groups = byParent(steps);

  return nodesOf(groups.get(null) ?? [], groups, { number: '', key: '' });
}

function findWhere(nodes: StepNode[], test: (node: StepNode) => boolean): StepNode | undefined {
  for (const node of nodes) {
    if (test(node))
      return node;

    const inner = findWhere(node.lanes.flatMap((lane) => lane.nodes), test);

    if (inner)
      return inner;
  }

  return undefined;
}

// The node with this key anywhere in the tree, lanes included.
export function findNode(nodes: StepNode[], key: string): StepNode | undefined {
  return findWhere(nodes, (node: StepNode) =>
    node.key === key);
}

export function findStepNode(nodes: StepNode[], id: string): StepNode | undefined {
  return findWhere(nodes, (node: StepNode) =>
    node.step.id === id);
}

export type MoveDirection = 'up' | 'down' | 'left' | 'right';

// Where a step sits: its lane, its place in it, and the same for the block that holds that lane.
type Where = {
  siblings: StepNode[];
  index: number;
  lane: Pick<StepLane, 'parentId' | 'branch'>;
  owner: Where | null;
};

const TOP_LANE = { parentId: null, branch: null } as const satisfies Pick<StepLane, 'parentId' | 'branch'>;

function locate(
  nodes: StepNode[],
  lane: Where['lane'],
  id: string,
  owner: Where | null,
): Where | undefined {
  for (const [index, node] of nodes.entries()) {
    const here = {
      siblings: nodes,
      index,
      lane,
      owner,
    } as const satisfies Where;

    if (node.step.id === id)
      return here;

    for (const inner of node.lanes) {
      const found = locate(inner.nodes, inner, id, here);

      if (found)
        return found;
    }
  }

  return undefined;
}

// The place a keyboard move takes the step to, or null when that move has nowhere to go. Up and down step one
// place in the lane, left leaves the block to just after it, right enters the block above at the end of its
// first lane.
export function keyboardSpot(nodes: StepNode[], id: string, direction: MoveDirection) {
  const where = locate(nodes, TOP_LANE, id, null);

  if (!where)
    return null;

  const { index, lane, owner, siblings } = where;

  if (direction === 'up' || direction === 'down') {
    const order = direction === 'up'
      ? index - 1
      : index + 1;

    return order < 0 || order >= siblings.length
      ? null
      : {
        parentId: lane.parentId,
        branch: lane.branch,
        order,
      } as const satisfies Spot;
  }

  if (direction === 'left') {
    return owner
      ? {
        parentId: owner.lane.parentId,
        branch: owner.lane.branch,
        order: owner.index + 1,
      } as const satisfies Spot
      : null;
  }

  const inner = siblings[index - 1]?.lanes[0];

  return inner
    ? {
      parentId: inner.parentId,
      branch: inner.branch,
      order: inner.nodes.length,
    } as const satisfies Spot
    : null;
}

// "Step 3 of 5": the step's place among the steps of its lane.
export function locateStep(nodes: StepNode[], id: string) {
  const where = locate(nodes, TOP_LANE, id, null);

  return where
    ? { position: where.index + 1, total: where.siblings.length } as const
    : null;
}
