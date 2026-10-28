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

// What a step the person adds from the palette looks like before they set it up. The first step of each list
// is the step itself; a block that runs its lanes through case steps comes with the cases it needs to have a
// lane to fill. The block settings are the engine's defaults (PipelineTreeTypes.cs).
import type { BlockKind, PipelineStep } from '@/api/pipelines';

import type { MintId } from './stepEdits';

// The blocks a person adds; a case step only ever comes with its switch or random block.
export const ADDABLE_BLOCKS = [
  'if',
  'switch',
  'random_branch',
  'loop',
  'try',
  'detached_step',
] as const satisfies BlockKind[];

export type AddableBlock = (typeof ADDABLE_BLOCKS)[number];

function blank(id: string, over: Partial<PipelineStep>): PipelineStep {
  return {
    id,
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    order: 0,
    action: { type: 'block' },
    condition: null,
    continue_on_error: false,
    stop_on_match: false,
    ...over,
  };
}

// One case step of a switch or random block.
type CaseOf = Pick<PipelineStep, 'parent_step_id' | 'order' | 'block_kind' | 'block_config'>;

function caseOf(fields: CaseOf, mint: MintId): PipelineStep {
  return blank(mint(), fields);
}

export function newAction(type: string, mint: MintId): PipelineStep[] {
  return [blank(mint(), { action: { type } })];
}

export function newBlock(kind: AddableBlock, mint: MintId): PipelineStep[] {
  const id = mint();

  if (kind === 'switch') {
    return [
      blank(id, { block_kind: kind, block_config: { value: '' } }),
      caseOf({
        parent_step_id: id,
        order: 0,
        block_kind: 'switch_case',
        block_config: {
          match: '',
          operator: 'eq',
          is_default: false,
        },
      }, mint),
    ];
  }

  if (kind === 'random_branch') {
    const path = (order: number): PipelineStep =>
      caseOf({
        parent_step_id: id,
        order,
        block_kind: 'random_case',
        block_config: { weight: 1 },
      }, mint);

    return [blank(id, { block_kind: kind }), path(0), path(1)];
  }

  const config = kind === 'loop'
    ? { mode: 'repeat', count: 1 }
    : null;

  return [blank(id, { block_kind: kind, block_config: config })];
}
