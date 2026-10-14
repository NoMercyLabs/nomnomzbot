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
import { nextTick, ref } from 'vue';

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
  };
}

const pipeline = {
  id: 'p1',
  name: 'Roast',
  isEnabled: true,
  graph: { steps: [step('a', 0), step('b', 1)] },
} as unknown as Pipeline;

function stepIds(steps: PipelineStep[]): (string | null)[] {
  return steps.map((item: PipelineStep) =>
    item.id);
}

describe('the step draft', () => {
  it('keeps unsaved edits when only the pipeline around the steps changes', async () => {
    const saved = ref<Pipeline | undefined>(pipeline);
    const draft = useStepDraft(saved);

    draft.remove('b');
    saved.value = { ...pipeline, isEnabled: false };
    await nextTick();

    expect(stepIds(draft.steps.value)).toEqual(['a']);
    expect(draft.dirty.value).toBe(true);
    expect(draft.canUndo.value).toBe(true);
  });

  it('takes the saved steps when the bot sends other steps', async () => {
    const saved = ref<Pipeline | undefined>(pipeline);
    const draft = useStepDraft(saved);

    draft.remove('b');
    saved.value = { ...pipeline, graph: { steps: [step('c', 0)] } };
    await nextTick();

    expect(stepIds(draft.steps.value)).toEqual(['c']);
    expect(draft.dirty.value).toBe(false);
    expect(draft.canUndo.value).toBe(false);
  });
});
