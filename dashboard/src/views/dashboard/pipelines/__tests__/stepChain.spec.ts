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

import type { PipelineActionDescriptor, PipelineStep } from '@/api/pipelines';
import { merge } from '@/lib/array';

import { declaredVariables, earlierTexts } from '../components/stepChain';
import { buildStepTree, findStepNode } from '../components/stepTree';

function step(over: Partial<PipelineStep>): PipelineStep {
  return {
    id: null,
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    order: 0,
    action: { type: 'send_message' },
    condition: null,
    continue_on_error: false,
    stop_on_match: false,
    ...over,
  };
}

function textField(name: string, repeatable = false): PipelineActionDescriptor['fields'][number] {
  return {
    name,
    kind: 'text',
    required: false,
    repeatable,
    description: null,
    templated: true,
    declaresVariable: false,
  } as PipelineActionDescriptor['fields'][number];
}

const descriptors = new Map<string, PipelineActionDescriptor>([
  ['send_message', { type: 'send_message', fields: [textField('message')] } as PipelineActionDescriptor],
  ['play_tts', { type: 'play_tts', fields: [textField('text'), textField('segments', true)] } as PipelineActionDescriptor],
]);

// 1 say "first", 2 check: then -> 2.1 say "{user} won", 3 read aloud, 4 say "after".
const steps: PipelineStep[] = [
  step({ id: 'first', action: { type: 'send_message', message: 'first' } }),
  step({
    id: 'check',
    order: 1,
    block_kind: 'if',
    action: { type: 'if' },
  }),
  step({
    id: 'won',
    parent_step_id: 'check',
    branch: 'then',
    action: { type: 'send_message', message: '{user} won' },
  }),
  step({
    id: 'tts',
    order: 2,
    action: {
      type: 'play_tts',
      text: '',
      segments: ['x'],
    },
  }),
  step({
    id: 'after',
    order: 3,
    action: { type: 'send_message', message: 'after' },
  }),
];

describe('the text earlier steps hold', () => {
  const tree = buildStepTree(steps);
  const keyOf = (id: string): string =>
    findStepNode(tree, id)!.key;

  it('lists the steps walked before this one, nearest first, with their unresolved template', () => {
    const found = earlierTexts(tree, keyOf('tts'), descriptors);

    expect(found.map((item) =>
      [item.number, item.field, item.text])).toEqual([
      ['2.1', 'message', '{user} won'],
      ['1', 'message', 'first'],
    ]);
  });

  it('leaves out later steps, empty text and repeated lists', () => {
    const found = earlierTexts(tree, keyOf('after'), descriptors);

    expect(found.map((item) =>
      item.step.id)).toEqual(['won', 'first']);
  });

  it('offers nothing to the first step', () => {
    expect(earlierTexts(tree, keyOf('first'), descriptors)).toEqual([]);
  });

  it('offers the variable an earlier step writes as a placeholder, so the later step reads its result', () => {
    const picks = new Map<string, PipelineActionDescriptor>([
      ['pick_from_list', { type: 'pick_from_list', fields: [{ ...textField('variable'), declaresVariable: true }] } as PipelineActionDescriptor],
    ]);
    const picked = buildStepTree([
      step({ id: 'pick', action: { type: 'pick_from_list', variable: 'line' } }),
      step({
        id: 'speak',
        order: 1,
        action: { type: 'send_message', message: '{line}' },
      }),
      step({ id: 'say', order: 2 }),
    ]);
    const both = new Map(merge([...picks], [...descriptors]));

    expect(earlierTexts(picked, findStepNode(picked, 'say')!.key, both).map((item) =>
      [item.step.id, item.text])).toEqual([['speak', '{line}']]);

    expect(declaredVariables(picked, findStepNode(picked, 'say')!.key, both)).toEqual(['line']);
    expect(declaredVariables(picked, findStepNode(picked, 'pick')!.key, both)).toEqual([]);
  });
});
