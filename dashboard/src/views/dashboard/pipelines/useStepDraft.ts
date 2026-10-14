// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The steps the person is changing on the workbench, apart from the saved pipeline until Save. Every edit
// replaces the list, so each one is a single undo step. A fresh load or a save starts a clean history.
import { useRefHistory } from '@vueuse/core';
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue';

import type { Pipeline, PipelineStep } from '@/api/pipelines';

import { duplicateStep, numberLanes, removeStep, withIds } from './components/stepEdits';

export type StepDraft = {
  steps: Ref<PipelineStep[]>;
  dirty: ComputedRef<boolean>;
  canUndo: Ref<boolean>;
  canRedo: Ref<boolean>;
  undo: () => void;
  redo: () => void;
  remove: (id: string) => void;
  duplicate: (id: string) => void;
};

const mint = (): string =>
  crypto.randomUUID();

export function useStepDraft(saved: Ref<Pipeline | undefined>): StepDraft {
  const steps = ref<PipelineStep[]>([]);
  const baseline = ref('[]');
  const history = useRefHistory(steps, { capacity: 50 });

  watch(saved, (pipeline: Pipeline | undefined) => {
    if (!pipeline) return;

    steps.value = numberLanes(withIds(pipeline.graph.steps, mint));
    baseline.value = JSON.stringify(steps.value);
    history.clear();
  }, { immediate: true });

  return {
    steps,
    dirty: computed(() => JSON.stringify(steps.value) !== baseline.value),
    canUndo: history.canUndo,
    canRedo: history.canRedo,
    undo: history.undo,
    redo: history.redo,
    remove: (id: string) => {
      steps.value = removeStep(steps.value, id);
    },
    duplicate: (id: string) => {
      steps.value = duplicateStep(steps.value, id, mint);
    },
  };
}
