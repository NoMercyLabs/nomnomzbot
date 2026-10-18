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

import { newAction, newBlock } from '../components/newSteps';

function counter(): () => string {
  let next = 0;

  return () => {
    next += 1;

    return `new${next}`;
  };
}

const kinds = (steps: PipelineStep[]): (string | null)[] =>
  steps.map((step) =>
    step.block_kind);

describe('a new step', () => {
  it('is an action of the picked type with every field still empty', () => {
    const [step] = newAction('send_message', counter());

    expect(step).toEqual({
      id: 'new1',
      parent_step_id: null,
      branch: null,
      block_kind: null,
      block_config: null,
      order: 0,
      action: { type: 'send_message' },
      condition: null,
      continue_on_error: false,
    });
  });

  it('is a block as the bot runs it, with the action type the KMP editor gives every block', () => {
    const [step] = newBlock('if', counter());

    expect(step?.block_kind).toBe('if');
    expect(step?.action).toEqual({ type: 'block' });
  });

  it('switches with one case that compares for equal, so the block has a lane to fill', () => {
    const steps = newBlock('switch', counter());

    expect(kinds(steps)).toEqual(['switch', 'switch_case']);
    expect(steps[0]?.block_config).toEqual({ value: '' });
    expect(steps[1]?.parent_step_id).toBe('new1');

    expect(steps[1]?.block_config).toEqual({
      match: '',
      operator: 'eq',
      is_default: false,
    });
  });

  it('picks at random between two equal paths', () => {
    const steps = newBlock('random_branch', counter());

    expect(kinds(steps)).toEqual(['random_branch', 'random_case', 'random_case']);

    expect(steps.slice(1).map((step) =>
      [step.parent_step_id, step.order, step.block_config])).toEqual([
      ['new1', 0, { weight: 1 }],
      ['new1', 1, { weight: 1 }],
    ]);
  });

  it('repeats once until the person sets a count', () => {
    expect(newBlock('loop', counter())[0]?.block_config).toEqual({ mode: 'repeat', count: 1 });
  });
});
