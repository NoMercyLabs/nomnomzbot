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

import type { JsonObject } from '@/lib/json';
// The workbench edits on the flat step list. Each returns a new list and leaves the given one as it was, so
// every edit is one undo step. The bot treats step ids as link keys only (save reinserts every row), so the
// workbench may mint its own.
import type { PipelineStep } from '@/api/pipelines';
import { merge } from '@/lib/array';

export type MintId = () => string;

// A place in one lane: the parent block and branch, and the order the step takes there.
export type Spot = {
  parentId: string | null;
  branch: string | null;
  order: number;
};

// Two steps in the same lane: same parent, same branch.
function sameLane(left: PipelineStep, right: PipelineStep): boolean {
  return laneOf(left) === laneOf(right);
}

// The step and every step inside it, at any depth.
function subtreeIds(steps: PipelineStep[], id: string): Set<string> {
  const ids = new Set([id]);
  let grew = true;

  while (grew) {
    grew = false;

    for (const step of steps) {
      if (step.id !== null && !ids.has(step.id) && ids.has(step.parent_step_id ?? '')) {
        ids.add(step.id);
        grew = true;
      }
    }
  }

  return ids;
}

function laneOf(step: Pick<PipelineStep, 'parent_step_id' | 'branch'>): string {
  return `${step.parent_step_id ?? ''}|${step.branch ?? ''}`;
}

// Each lane numbered 0, 1, 2 in the order the engine runs it. A graph saved without orders reads them all as
// 0, and the bot then runs the lane in list order, so the list position breaks a tie.
export function numberLanes(steps: PipelineStep[]): PipelineStep[] {
  const ranked = steps
    .map((step, index) =>
      ({ step, index }))
    .sort((left, right) =>
      left.step.order - right.step.order || left.index - right.index);
  const next = new Map<string, number>();
  const orders = new Map<number, number>();

  for (const { step, index } of ranked) {
    const order = next.get(laneOf(step)) ?? 0;

    orders.set(index, order);
    next.set(laneOf(step), order + 1);
  }

  return steps.map((step, index) =>
    ({ ...step, order: orders.get(index) ?? step.order }));
}

export function withIds(steps: PipelineStep[], mint: MintId): PipelineStep[] {
  return steps.map((step) =>
    step.id === null
      ? { ...step, id: mint() }
      : step);
}

export function removeStep(steps: PipelineStep[], id: string): PipelineStep[] {
  const target = steps.find((step) =>
    step.id === id);

  if (!target)
    return steps;

  const gone = subtreeIds(steps, id);

  return steps
    .filter((step) =>
      !gone.has(step.id ?? ''))
    .map((step) =>
      sameLane(step, target) && step.order > target.order
        ? { ...step, order: step.order - 1 }
        : step);
}

// The place after the last step of a lane.
export function laneEnd(
  steps: PipelineStep[],
  parentId: string | null,
  branch: string | null,
) {
  const lane = laneOf({ parent_step_id: parentId, branch });
  const orders = steps
    .filter((step) =>
      laneOf(step) === lane)
    .map((step) =>
      step.order);

  return {
    parentId,
    branch,
    order: Math.max(-1, ...orders) + 1,
  } as const satisfies Spot;
}

// New steps placed at the spot: the first is the step itself, the rest sit inside it. The later steps of the
// lane move down one.
export function insertSteps(
  steps: PipelineStep[],
  added: PipelineStep[],
  spot: Spot,
): PipelineStep[] {
  const [root, ...inner] = added;

  if (!root)
    return steps;

  const placed = {
    ...root,
    parent_step_id: spot.parentId,
    branch: spot.branch,
    order: spot.order,
  };
  const shifted = steps.map((step) =>
    sameLane(step, placed) && step.order >= spot.order
      ? { ...step, order: step.order + 1 }
      : step);

  return [...merge(shifted, [placed], inner)];
}

// The ids of one lane in run order, without the step that is leaving it.
function laneIds(steps: PipelineStep[], lane: string, leaving: string): string[] {
  return steps
    .filter((step) =>
      laneOf(step) === lane && step.id !== leaving)
    .sort((left, right) =>
      left.order - right.order)
    .map((step) =>
      step.id ?? '');
}

// The step, with everything inside it, taken out and put at the spot. The spot order is the place the step
// ends up at in that lane. Both lanes are numbered again; a spot inside the step itself changes nothing.
export function moveStep(steps: PipelineStep[], id: string, spot: Spot): PipelineStep[] {
  const target = steps.find((step) =>
    step.id === id);

  if (!target || subtreeIds(steps, id).has(spot.parentId ?? ''))
    return steps;

  const destination = laneIds(
    steps,
    laneOf({ parent_step_id: spot.parentId, branch: spot.branch }),
    id,
  );
  const origin = laneIds(steps, laneOf(target), id);

  const place = Math.max(spot.order, 0);

  destination.splice(Math.min(place, destination.length), 0, id);

  const orders = new Map<string, number>();

  for (const lane of [origin, destination]) {
    lane.forEach((stepId, index) =>
      orders.set(stepId, index));
  }

  return steps.map((step) => {
    const order = orders.get(step.id ?? '') ?? step.order;

    return step.id === id
      ? {
          ...step,
          parent_step_id: spot.parentId,
          branch: spot.branch,
          order,
        }
      : { ...step, order };
  });
}

// Settings to change on one step: the named action and block settings change, a cleared one (undefined) goes.
export type StepPatch = {
  action?: JsonObject;
  block_config?: JsonObject;
  continue_on_error?: boolean;
  stop_on_match?: boolean;
};

function withSettings<T extends object>(current: T, change: JsonObject): T {
  const next: JsonObject = { ...current, ...change };

  for (const [key, value] of Object.entries(change)) {
    if (value === undefined) {
      delete next[key];
    }
  }

  return next as T;
}

function patched(step: PipelineStep, patch: StepPatch) {
  return {
    ...step,
    action: withSettings(step.action, patch.action ?? {}),
    block_config: patch.block_config
      ? withSettings(step.block_config ?? {}, patch.block_config)
      : step.block_config,
    continue_on_error: patch.continue_on_error ?? step.continue_on_error,
    stop_on_match: patch.stop_on_match ?? step.stop_on_match,
  } as const satisfies PipelineStep;
}

export function patchStep(steps: PipelineStep[], id: string, patch: StepPatch): PipelineStep[] {
  return steps.map((step) =>
    step.id === id
      ? patched(step, patch)
      : step);
}

// A step is plain JSON; a JSON copy also reads through the reactive proxy the draft holds it in, which
// structuredClone refuses.
function copyOf(step: PipelineStep, target: PipelineStep, ids: Map<string, string>) {
  const root = step.id === target.id;

  return {
    ...(JSON.parse(JSON.stringify(step)) as PipelineStep),
    id: ids.get(step.id ?? '') ?? null,
    parent_step_id: root
      ? step.parent_step_id
      : (ids.get(step.parent_step_id ?? '') ?? null),
    order: root
      ? target.order + 1
      : step.order,
  } as const satisfies PipelineStep;
}

// A copy of the step, inner steps included, placed right after it under new ids.
export function duplicateStep(steps: PipelineStep[], id: string, mint: MintId): PipelineStep[] {
  const target = steps.find((step) =>
    step.id === id);

  if (!target)
    return steps;

  const copied = subtreeIds(steps, id);
  const inside = steps.filter((step) =>
    copied.has(step.id ?? ''));
  const ids = new Map(inside.map((step) =>
    [step.id ?? '', mint()]));
  const shifted = steps.map((step) =>
    sameLane(step, target) && step.order > target.order
      ? { ...step, order: step.order + 1 }
      : step);
  const copies = inside.map((step) =>
    copyOf(step, target, ids));

  return [...merge(shifted, copies)];
}
