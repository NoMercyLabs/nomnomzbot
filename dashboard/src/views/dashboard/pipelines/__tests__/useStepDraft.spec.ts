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
import { nextTick, shallowRef } from 'vue';

import type { Pipeline, PipelineStep } from '@/api/pipelines';

import { useStepDraft } from '../useStepDraft';

function step(id: string, order: number): PipelineStep {
  return {
    id,
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    order,
    action: { type: 'wait' },
    condition: null,
    continue_on_error: false,
    stop_on_match: false,
  };
}

const pipeline = {
  id: 'p1',
  name: 'Roast',
  isEnabled: true,
  graph: { steps: [step('a', 0), step('b', 1)] },
} as unknown as Pipeline;

function stepIds(steps: PipelineStep[]): (string | null)[] {
  return steps.map((item) =>
    item.id);
}

describe('the step draft', () => {
  it('keeps unsaved edits when only the pipeline around the steps changes', async () => {
    const saved = shallowRef<Pipeline | undefined>(pipeline);
    const draft = useStepDraft(saved);

    draft.remove('b');
    saved.value = { ...pipeline, isEnabled: false };
    await nextTick();

    expect(stepIds(draft.steps.value)).toEqual(['a']);
    expect(draft.dirty.value).toBe(true);
    expect(draft.canUndo.value).toBe(true);
  });

  it('takes the saved steps when the bot sends other steps', async () => {
    const saved = shallowRef<Pipeline | undefined>(pipeline);
    const draft = useStepDraft(saved);

    draft.remove('b');
    saved.value = { ...pipeline, graph: { steps: [step('c', 0)] } };
    await nextTick();

    expect(stepIds(draft.steps.value)).toEqual(['c']);
    expect(draft.dirty.value).toBe(false);
    expect(draft.canUndo.value).toBe(false);
  });
});
