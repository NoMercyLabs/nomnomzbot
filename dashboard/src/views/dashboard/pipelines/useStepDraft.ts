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

import {
  duplicateStep,
  insertSteps,
  laneEnd,
  numberLanes,
  patchStep,
  removeStep,
  withIds,
  type MintId,
  type Spot,
  type StepPatch,
} from './components/stepEdits';

// Makes the new step, with the steps inside it, from fresh ids.
export type MakeSteps = (mint: MintId) => PipelineStep[];

export type StepDraft = {
  steps: Ref<PipelineStep[]>;
  dirty: ComputedRef<boolean>;
  canUndo: Ref<boolean>;
  canRedo: Ref<boolean>;
  undo: () => void;
  redo: () => void;
  remove: (id: string) => void;
  duplicate: (id: string) => void;
  patch: (id: string, patch: StepPatch) => void;
  // Adds at the spot, or at the end of the top level; gives the new step's id.
  add: (make: MakeSteps, spot?: Spot) => string | null;
};

const mint = (): string =>
  crypto.randomUUID();

export function useStepDraft(saved: Ref<Pipeline | undefined>): StepDraft {
  const steps = ref<PipelineStep[]>([]);
  const baseline = ref('[]');
  // Recorded at once: a deferred record of the load would land after clear() and let Undo empty the list.
  const history = useRefHistory(steps, { capacity: 50, flush: 'sync' });

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
    patch: (id: string, patch: StepPatch) => {
      steps.value = patchStep(steps.value, id, patch);
    },
    add: (make: MakeSteps, spot?: Spot) => {
      const added = make(mint);

      steps.value = insertSteps(steps.value, added, spot ?? laneEnd(steps.value, null, null));

      return added[0]?.id ?? null;
    },
  };
}
