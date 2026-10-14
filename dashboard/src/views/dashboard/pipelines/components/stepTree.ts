// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The flat step list the bot stores, read as the tree the engine walks (PipelineEngine.ExecuteBlockAsync):
// if and try split their children by branch then/else, switch and random_branch hold one case step per
// lane, and loop and detached_step run all their children as one lane.
import type { BlockKind, PipelineStep } from '@/api/pipelines';

export type LaneKind = 'yes' | 'no' | 'works' | 'fails' | 'each' | 'case' | 'path' | 'alone';

export type StepLane = {
  kind: LaneKind;
  // The case step of a switch or random lane: it carries the match or the weight.
  head: PipelineStep | null;
  nodes: StepNode[];
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
function laneStart(place: Place, lane: number): Place {
  return { number: `${place.number}.`, key: `${place.key}:${lane}.` };
}

function nodesOf(steps: PipelineStep[], groups: Siblings, start: Place): StepNode[] {
  return steps.map((step: PipelineStep, index: number) =>
    nodeOf(step, groups, { number: `${start.number}${index + 1}`, key: `${start.key}${index + 1}` }));
}

function lanesOf(step: PipelineStep, groups: Siblings, place: Place): StepLane[] {
  const children = childrenOf(step, groups);

  if (step.block_kind === 'if' || step.block_kind === 'try') {
    return BRANCH_LANES[step.block_kind].map((kind: BranchLane, lane: number) => {
      const inLane = children.filter((child: PipelineStep) =>
        child.branch === BRANCH_OF[kind]);

      return {
        kind,
        head: null,
        nodes: nodesOf(inLane, groups, laneStart(place, lane)),
      };
    });
  }

  if (step.block_kind === 'switch' || step.block_kind === 'random_branch') {
    const kind = step.block_kind === 'switch'
      ? 'case'
      : 'path';

    return children.map((head: PipelineStep, lane: number) => ({
      kind,
      head,
      nodes: nodesOf(childrenOf(head, groups), groups, laneStart(place, lane)),
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
    }];
  }

  return [];
}

function nodeOf(step: PipelineStep, groups: Siblings, place: Place): StepNode {
  return {
    step,
    ...place,
    lanes: lanesOf(step, groups, place),
  };
}

export function buildStepTree(steps: PipelineStep[]): StepNode[] {
  const groups = byParent(steps);

  return nodesOf(groups.get(null) ?? [], groups, { number: '', key: '' });
}

// The node with this key anywhere in the tree, lanes included.
export function findNode(nodes: StepNode[], key: string): StepNode | undefined {
  for (const node of nodes) {
    if (node.key === key)
      return node;

    const inner = findNode(node.lanes.flatMap((lane: StepLane) => lane.nodes), key);

    if (inner)
      return inner;
  }

  return undefined;
}
