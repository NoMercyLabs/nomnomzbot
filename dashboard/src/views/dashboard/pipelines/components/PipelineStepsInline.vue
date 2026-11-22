<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The steps of one saved pipeline as a list in place, for a form that picked it (the shared target picker): the
// workbench's own step card, details and palette, with the steps on top and the settings of the open step under
// them. A pipeline is shared, so a change to it is a visible act: Save and Discard show only while the draft
// differs, and a role without pipelines:write sees the steps locked.
import Add from '~icons/nnz/add';
import { toast } from 'vue-sonner';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChannelAccess } from '@/api/access';
import {
  usePipeline,
  usePipelineCatalog,
  useSavePipeline,
  type PipelineStep,
} from '@/api/pipelines';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { ensureWords, schemaWords } from '@/i18n';
import { merge } from '@/lib/array';
import { errorText } from '@/lib/errors';

import { newAction, newBlock, type AddableBlock } from './newSteps';
import { declaredVariables, earlierTexts } from './stepChain';
import { laneEnd, type MintId, type Spot, type StepPatch } from './stepEdits';
import {
  buildStepTree,
  findNode,
  findStepNode,
  keyboardSpot,
  type MoveDirection,
  type StepLane,
} from './stepTree';
import StepCard from './StepCard.vue';
import StepInspector from './StepInspector.vue';
import StepPalette from './StepPalette.vue';
import { vLaneDrag } from './useStepDrag';
import { useStepDraft } from '../useStepDraft';

const props = defineProps<{
  pipelineId: string;
}>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const page = 'dashboard.pipelines.workbench';

const pipeline = usePipeline(channelId, () => props.pipelineId);
const catalog = usePipelineCatalog(channelId);
const saving = useSavePipeline(channelId);
const access = useChannelAccess(channelId);
const canWrite = computed(() => access.can('pipelines:write'));
const draft = useStepDraft(pipeline.data);

// The words of the step parts belong to the pipelines pages; a form elsewhere loads them itself. The translation
// lookup does not notify a computed, so the words arriving flips this flag.
const wordsReady = ref(false);

void Promise.all([
  ensureWords((locale) => import(`../i18n/${locale}.json`)),
  ensureWords(schemaWords),
]).then(() => {
  wordsReady.value = true;
});

const tree = computed(() => buildStepTree(draft.steps.value));

const descriptors = computed(
  () =>
    new Map(
      (catalog.data.value?.actions ?? []).map((action) => [
        action.type,
        action,
      ]),
    ),
);

// The open step is kept by its id: a move or an add changes the place the cards hold.
const openId = ref<string | null>(null);
const selected = computed(() =>
  openId.value === null
    ? undefined
    : findStepNode(tree.value, openId.value));
const selectedKey = computed(() => selected.value?.key ?? null);

function pick(key: string): void {
  const id = findNode(tree.value, key)?.step.id ?? null;

  openId.value = id === openId.value
    ? null
    : id;
}

function patch(change: StepPatch): void {
  const id = selected.value?.step.id;

  if (id) {
    draft.patch(id, change);
  }
}

function duplicate(): void {
  const id = selected.value?.step.id;

  if (id) {
    draft.duplicate(id);
  }
}

function remove(): void {
  const id = selected.value?.step.id;

  if (!id) return;

  openId.value = null;
  draft.remove(id);
}

function moveTo(stepId: string, to: Spot): void {
  draft.move(stepId, to);
}

function nudge(stepId: string, direction: MoveDirection): void {
  const to = keyboardSpot(tree.value, stepId, direction);

  if (to) {
    moveTo(stepId, to);
  }
}

const earlier = computed(() =>
  selectedKey.value === null
    ? []
    : earlierTexts(tree.value, selectedKey.value, descriptors.value));

const variables = computed(() => {
  if (selectedKey.value === null)
    return [];

  const inputs = (pipeline.data.value?.inputs ?? []).map((input) => input.name).filter(Boolean);
  const declared = declaredVariables(tree.value, selectedKey.value, descriptors.value);

  return Array.from(merge(inputs, declared));
});

// The palette opens under the list; a lane button names the lane a pick goes to, none adds at the end.
const adding = ref(false);
const target = ref<{ lane: StepLane; label: string } | null>(null);

function addTo(lane: StepLane, label: string): void {
  target.value = { lane, label };
  adding.value = true;
}

function spot(): Spot | undefined {
  const lane = target.value?.lane;

  return lane
    ? laneEnd(draft.steps.value, lane.parentId, lane.branch)
    : undefined;
}

// A new step opens in the details at once, so it is clear where to set it up.
function added(id: string | null): void {
  adding.value = false;
  target.value = null;
  openId.value = id;
}

function addAction(type: string): void {
  const make = (mint: MintId): PipelineStep[] => newAction(type, mint);
  const id = draft.add(make, spot());

  added(id);
}

function addBlock(kind: AddableBlock): void {
  const make = (mint: MintId): PipelineStep[] => newBlock(kind, mint);
  const id = draft.add(make, spot());

  added(id);
}

function discard(): void {
  while (draft.canUndo.value) {
    draft.undo();
  }

  openId.value = null;
}

async function save(): Promise<void> {
  // A field hands its typed value over when it is left, so a press on Save leaves it first.
  if (document.activeElement instanceof HTMLElement) {
    document.activeElement.blur();
  }

  if (!draft.dirty.value || !canWrite.value) return;

  try {
    await saving.mutateAsync({ id: props.pipelineId, graph: { steps: draft.steps.value } });

    toast.success(t(`${page}.saved`));
  } catch (error: unknown) {
    toast.error(error instanceof Error
      ? errorText(error, t)
      : t(`${page}.saveFailed`));
  }
}
</script>

<template>
  <section
    v-if="wordsReady && access.can('pipelines:read') && pipeline.data.value"
    data-pipeline-steps
    class="mt-2 flex min-w-0 flex-col gap-3"
  >
    <ol
      v-if="tree.length > 0"
      v-lane-drag="{ parentId: null, branch: null, enabled: canWrite, onMove: moveTo }"
      class="flex flex-col gap-2"
    >
      <StepCard
        v-for="node in tree"
        :key="node.key"
        :node="node"
        :selected-key="selectedKey"
        :descriptors="descriptors"
        :read-only="!canWrite"
        :marks="null"
        @select="pick"
        @add-to="addTo"
        @move="moveTo"
        @nudge="nudge"
      />
    </ol>
    <p v-else class="text-sm text-muted-foreground">
      {{ $t(`${page}.empty`) }}
    </p>
    <Button
      v-if="canWrite"
      data-steps-add
      type="button"
      variant="outline"
      size="sm"
      class="w-fit"
      @click="adding = !adding"
    >
      <Add aria-hidden="true" />
      {{ $t(`${page}.palette.addEnd`) }}
    </Button>
    <StepPalette
      v-if="canWrite && adding"
      class="h-80"
      :actions="catalog.data.value?.actions ?? []"
      :read-only="!canWrite"
      :target="target?.label ?? null"
      @action="addAction"
      @block="addBlock"
      @release="target = null"
    />
    <StepInspector
      v-if="selected"
      :step="selected.step"
      :number="selected.number"
      :descriptor="descriptors.get(selected.step.action.type)"
      :read-only="!canWrite"
      :earlier="earlier"
      :variables="variables"
      @duplicate="duplicate"
      @remove="remove"
      @patch="patch"
    />
    <!-- Save is the one primary action; Discard is outline. -->
    <div v-if="canWrite && draft.dirty.value" class="flex flex-wrap justify-end gap-2">
      <Button data-steps-discard
              type="button"
              variant="outline"
              size="sm"
              @click="discard"
      >
        {{ $t(`${page}.draft.discard`) }}
      </Button>
      <Button
        data-steps-save
        type="button"
        size="sm"
        :disabled="saving.isPending.value"
        @click="save"
      >
        {{ $t(`${page}.save`) }}
      </Button>
    </div>
  </section>
</template>
