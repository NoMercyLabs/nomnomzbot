<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// A pipeline definition's draft in the channel pipeline step editor (the same cards, palette and details), so the
// operator edits steps, not JSON. Save hands the parent the payload in the shape the server fans out; Edit JSON
// is the raw fallback. Save is the one primary action; Edit JSON and Close are outline and ghost.
import Add from '~icons/nnz/add';
import Cross from '~icons/nnz/cross';
import { computed, ref } from 'vue';

import { usePipelineCatalog, type PipelineGraph, type PipelineStep } from '@/api/pipelines';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { ensureWords, schemaWords } from '@/i18n';
import StepCard from '@/views/dashboard/pipelines/components/StepCard.vue';
import StepInspector from '@/views/dashboard/pipelines/components/StepInspector.vue';
import StepPalette from '@/views/dashboard/pipelines/components/StepPalette.vue';
import { newAction, newBlock, type AddableBlock } from '@/views/dashboard/pipelines/components/newSteps';
import { declaredVariables, earlierTexts } from '@/views/dashboard/pipelines/components/stepChain';
import {
  laneEnd,
  type MintId,
  type Spot,
  type StepPatch,
} from '@/views/dashboard/pipelines/components/stepEdits';
import {
  buildStepTree,
  findNode,
  findStepNode,
  keyboardSpot,
  type MoveDirection,
  type StepLane,
} from '@/views/dashboard/pipelines/components/stepTree';
import { pipelineWords } from '@/views/dashboard/pipelines/components/stepWords';
import { vLaneDrag } from '@/views/dashboard/pipelines/components/useStepDrag';
import { useStepDraft } from '@/views/dashboard/pipelines/useStepDraft';

import { writePipelinePayload } from './pipelinePayload';

const props = defineProps<{
  title: string;
  graph: PipelineGraph;
  // Keeps the payload; resolves to the message shown after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{
  close: [];
  // Hands the draft over as JSON for the raw editor.
  editJson: [payloadJson: string];
}>();

const { channelId } = useCurrentChannel();
const page = 'dashboard.pipelines.workbench';

const catalog = usePipelineCatalog(channelId);
const draft = useStepDraft(computed(() => ({ id: 'definition', graph: props.graph })));

// The words of the step parts belong to the pipelines pages; this form loads them itself.
const wordsReady = ref(false);

void Promise.all([
  ensureWords(pipelineWords),
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

const openId = ref<string | null>(null);
const selected = computed(() =>
  openId.value === null
    ? undefined
    : findStepNode(tree.value, openId.value));
const selectedKey = computed(() => selected.value?.key ?? null);
const earlier = computed(() =>
  selectedKey.value === null
    ? []
    : earlierTexts(tree.value, selectedKey.value, descriptors.value));
const variables = computed(() =>
  selectedKey.value === null
    ? []
    : declaredVariables(tree.value, selectedKey.value, descriptors.value));

const adding = ref(false);
const target = ref<{ lane: StepLane; label: string } | null>(null);
const message = ref('');
const saving = ref(false);

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

function currentJson(): string {
  return writePipelinePayload(draft.steps.value);
}

async function save(): Promise<void> {
  // A field hands its typed value over when it is left, so a press on Save leaves it first.
  if (document.activeElement instanceof HTMLElement) {
    document.activeElement.blur();
  }

  saving.value = true;
  message.value = await props.keep(currentJson());
  saving.value = false;
}
</script>

<template>
  <Dialog :open="true" @update:open="emit('close')">
    <DialogContent
      data-pipeline-payload-editor
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-3xl"
    >
      <div class="flex flex-none items-start justify-between gap-4 px-6 pt-6 pb-4">
        <div class="grid gap-1.5">
          <DialogTitle class="text-lg font-semibold">{{ title }}</DialogTitle>
          <DialogDescription class="text-sm text-muted-foreground">
            {{ $t("dashboard.admin.content.pipelineEditor.description") }}
          </DialogDescription>
        </div>
        <DialogClose as-child>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="$t('common.close')"
          >
            <Cross class="size-5" aria-hidden="true" />
          </Button>
        </DialogClose>
      </div>
      <div v-if="wordsReady" class="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto px-6 pb-4">
        <ol
          v-if="tree.length > 0"
          v-lane-drag="{ parentId: null, branch: null, enabled: true, onMove: moveTo }"
          class="flex flex-col gap-2"
        >
          <StepCard
            v-for="node in tree"
            :key="node.key"
            :node="node"
            :selected-key="selectedKey"
            :descriptors="descriptors"
            :read-only="false"
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
          v-if="adding"
          class="h-80"
          :actions="catalog.data.value?.actions ?? []"
          :read-only="false"
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
          :read-only="false"
          :earlier="earlier"
          :variables="variables"
          @duplicate="duplicate"
          @remove="remove"
          @patch="patch"
        />
      </div>
      <p
        v-if="message"
        data-pipeline-message
        class="flex-none px-6 pb-4 text-sm text-muted-foreground"
        role="status"
      >
        {{ message }}
      </p>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button
          data-pipeline-edit-json
          type="button"
          variant="outline"
          class="sm:mr-auto"
          @click="emit('editJson', currentJson())"
        >
          {{ $t("dashboard.admin.content.pipelineEditor.editJson") }}
        </Button>
        <Button
          data-pipeline-save
          type="button"
          :disabled="saving"
          @click="save"
        >
          {{ $t("dashboard.admin.content.draft.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
