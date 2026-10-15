<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One pipeline on the workbench (5-workbench.html): the steps to add, what starts it, its steps in the order
// the engine walks them with every check split into lanes, and the picked step in detail beside the canvas.
// Narrower than a laptop the palette and the details slide in as drawers, and the back button closes them.
import Add from '~icons/nnz/add';
import ArrowLeft from '~icons/nnz/arrow-left';
import Undo from '~icons/nnz/arrow-back-up';
import Redo from '~icons/nnz/arrow-forward-up';
import Flag from '~icons/nnz/flag';
import Lightning from '~icons/nnz/lightning';
import Play from '~icons/nnz/play';
import { toast } from 'vue-sonner';
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router';

import { useChannelAccess } from '@/api/access';
import {
  useBlastRadius,
  usePipeline,
  usePipelineCatalog,
  useSavePipeline,
  useTestRunPipeline,
  type PipelineActionDescriptor,
  type PipelineStep,
  type PipelineTestRun,
} from '@/api/pipelines';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePageView } from '@/composables/usePageView';
import { errorText } from '@/lib/errors';
import { usePanelState } from '@/composables/usePanelState';
import { useShortcut } from '@/composables/useShortcut';

import { newAction, newBlock, type AddableBlock } from './components/newSteps';
import PipelineMenu from './components/PipelineMenu.vue';
import RunsPanel from './components/RunsPanel.vue';
import StepCard from './components/StepCard.vue';
import StepInspector from './components/StepInspector.vue';
import { laneEnd, type MintId, type Spot, type StepPatch } from './components/stepEdits';
import StepPalette from './components/StepPalette.vue';
import { runMarks, runSummary } from './components/stepRun';
import TestRunInput from './components/TestRunInput.vue';
import { buildStepTree, findNode, findStepNode, type StepLane } from './components/stepTree';
import { startSources, type StartSource } from './components/stepWords';
import { useStepDraft } from './useStepDraft';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const { channelId } = useCurrentChannel();
const id = computed(() => String(route.params.id));
const page = 'dashboard.pipelines.workbench';

// Steps edits the pipeline; Runs lists what it did when it really ran, in the canvas's place (mockup #runs-view).
const VIEWS = ['steps', 'runs'] as const satisfies readonly string[];
const view = usePageView(VIEWS);
const views = computed<PageTab[]>(() =>
  VIEWS.map((value) => ({ value, label: t(`${page}.views.${value}`) })));

const pipeline = usePipeline(channelId, id);
const catalog = usePipelineCatalog(channelId);
const radius = useBlastRadius(channelId, id);
const saving = useSavePipeline(channelId);
const access = useChannelAccess(channelId);
const canWrite = computed(() => access.can('pipelines:write'));

const draft = useStepDraft(pipeline.data);
const state = usePanelState(pipeline);
const tree = computed(() => buildStepTree(draft.steps.value));
const stepCount = computed(() => draft.steps.value.length);

const descriptors = computed(() =>
  new Map((catalog.data.value?.actions ?? []).map((action: PipelineActionDescriptor) =>
    [action.type, action])));

const sources = computed(() =>
  startSources(radius.data.value));

const starts = computed(() => {
  if (sources.value.length === 0)
    return t('dashboard.pipelines.workbench.starts.none');

  const named = sources.value.map((source: StartSource) =>
    t(`dashboard.pipelines.workbench.starts.${source.kind}`, { name: source.name }));

  return t('dashboard.pipelines.workbench.starts.sentence', { sources: named.join(', ') });
});

const selectedKey = ref<string | null>(null);
const selected = computed(() =>
  selectedKey.value === null
    ? undefined
    : findNode(tree.value, selectedKey.value));

// The drawer closes by back, a tap outside or Escape; closing it lets go of the step.
const inspecting = computed<boolean>({
  get: () => selected.value !== undefined,
  set: (open: boolean) => {
    if (!open) {
      selectedKey.value = null;
    }
  },
});

function pick(key: string): void {
  selectedKey.value = key === selectedKey.value
    ? null
    : key;
}

function duplicate(): void {
  const step = selected.value?.step;

  if (step?.id) {
    draft.duplicate(step.id);
  }
}

function patch(change: StepPatch): void {
  const step = selected.value?.step;

  if (step?.id) {
    draft.patch(step.id, change);
  }
}

function remove(): void {
  const step = selected.value?.step;

  if (!step?.id) return;

  selectedKey.value = null;
  draft.remove(step.id);
  toast(t(`${page}.removed`), { action: { label: t(`${page}.undo`), onClick: draft.undo } });
}

const paletteOpen = ref(false);
const palette = useTemplateRef<InstanceType<typeof StepPalette>>('palette');

// The lane the person chose to add to, with its words; none adds at the end of the top level.
const target = ref<{ lane: StepLane; label: string } | null>(null);

watch(paletteOpen, (open: boolean) => {
  if (!open) {
    target.value = null;
  }
});

async function openPalette(): Promise<void> {
  paletteOpen.value = true;
  await nextTick();
  palette.value?.focus();
}

function addTo(lane: StepLane, label: string): void {
  target.value = { lane, label };
  void openPalette();
}

function spot(): Spot | undefined {
  const lane = target.value?.lane;

  return lane
    ? laneEnd(draft.steps.value, lane.parentId, lane.branch)
    : undefined;
}

// A new step opens in the details at once, so it is clear where to set it up.
function added(id: string | null): void {
  const node = id === null
    ? undefined
    : findStepNode(tree.value, id);

  paletteOpen.value = false;
  target.value = null;
  selectedKey.value = node?.key ?? null;
}

function addAction(type: string): void {
  const make = (mint: MintId): PipelineStep[] =>
    newAction(type, mint);
  const id = draft.add(make, spot());

  added(id);
}

function addBlock(kind: AddableBlock): void {
  const make = (mint: MintId): PipelineStep[] =>
    newBlock(kind, mint);
  const id = draft.add(make, spot());

  added(id);
}

// The test run dry-runs the draft as it is now: side effects are only recorded. Its marks show on the canvas
// until the draft changes, because then they would describe steps that are no longer there.
const testing = useTestRunPipeline(channelId);
const lastRun = ref<PipelineTestRun | null>(null);
const runCount = ref(0);
const runInput = ref<Record<string, string>>({});
const marks = computed(() =>
  lastRun.value === null
    ? null
    : runMarks(lastRun.value));
const summary = computed(() =>
  lastRun.value === null
    ? null
    : runSummary(lastRun.value));

watch(draft.steps, () => {
  lastRun.value = null;
});

async function testRun(): Promise<void> {
  try {
    lastRun.value = await testing.mutateAsync({
      id: id.value,
      variables: runInput.value,
      graph: { steps: draft.steps.value },
    });

    runCount.value += 1;
  } catch (error: unknown) {
    toast.error(error instanceof Error
      ? errorText(error, t)
      : t(`${page}.run.failedToStart`));
  }
}

function toggleRun(): void {
  if (lastRun.value === null) {
    void testRun();
  } else {
    lastRun.value = null;
  }
}

async function save(): Promise<void> {
  // A field hands its typed value over when it is left, so Ctrl+S in a field leaves it first.
  if (document.activeElement instanceof HTMLElement) {
    document.activeElement.blur();
  }

  if (!draft.dirty.value || !canWrite.value) return;

  try {
    await saving.mutateAsync({ id: id.value, graph: { steps: draft.steps.value } });
    toast.success(t(`${page}.saved`));
  } catch (error: unknown) {
    toast.error(error instanceof Error
      ? errorText(error, t)
      : t(`${page}.saveFailed`));
  }
}

useShortcut({
  key: 'z',
  mod: true,
  label: `${page}.shortcuts.undo`,
}, (event: KeyboardEvent) => {
  if (event.shiftKey) {
    draft.redo();
  } else {
    draft.undo();
  }
});

useShortcut({
  key: 'y',
  mod: true,
  label: `${page}.shortcuts.redo`,
}, draft.redo);

useShortcut({
  key: 'a',
  label: `${page}.shortcuts.add`,
}, () => {
  if (canWrite.value) {
    void openPalette();
  }
});

useShortcut({
  key: 's',
  mod: true,
  label: `${page}.shortcuts.save`,
  inFields: true,
}, () => {
  void save();
});

// Unsaved changes hold the way out until the person says to leave without them.
const leavingTo = ref<string | null>(null);
let leaveAnyway = false;

onBeforeRouteLeave((to) => {
  if (leaveAnyway || !draft.dirty.value)
    return true;

  leavingTo.value = to.fullPath;

  return false;
});

function leave(): void {
  const to = leavingTo.value;

  leaveAnyway = true;
  leavingTo.value = null;

  if (to) {
    void router.push(to);
  }
}

// A deleted pipeline has nothing left to save, so its unsaved edits do not hold the way out.
function leaveDeleted(): void {
  leaveAnyway = true;
  void router.push({ name: 'dashboard-pipelines' });
}

const leaveOpen = computed<boolean>({
  get: () => leavingTo.value !== null,
  set: (open: boolean) => {
    if (!open) {
      leavingTo.value = null;
    }
  },
});
</script>

<template>
  <PageHeader :title="pipeline.data.value?.name ?? $t('dashboard.pipelines.title')" :crumb="$t('dashboard.pipelines.title')">
    <template v-if="pipeline.data.value && !pipeline.data.value.isEnabled" #status>
      <StatusBadge tone="off" :label="$t('dashboard.pipelines.workbench.off')" />
    </template>
    <template #actions>
      <span class="flex items-center gap-1" :title="canWrite ? undefined : $t('dashboard.pipelines.noWrite')">
        <Button
          data-detail-back
          type="button"
          variant="ghost"
          size="sm"
          class="md:hidden"
          @click="router.push({ name: 'dashboard-pipelines' })"
        >
          <ArrowLeft aria-hidden="true" />
          {{ $t('dashboard.pipelines.back') }}
        </Button>
        <Button
          data-workbench-undo
          type="button"
          variant="ghost"
          size="icon-sm"
          :disabled="!canWrite || !draft.canUndo.value"
          :aria-label="$t('dashboard.pipelines.workbench.undo')"
          :title="$t('dashboard.pipelines.workbench.undo')"
          @click="draft.undo()"
        >
          <Undo aria-hidden="true" />
        </Button>
        <Button
          data-workbench-redo
          type="button"
          variant="ghost"
          size="icon-sm"
          :disabled="!canWrite || !draft.canRedo.value"
          :aria-label="$t('dashboard.pipelines.workbench.redo')"
          :title="$t('dashboard.pipelines.workbench.redo')"
          @click="draft.redo()"
        >
          <Redo aria-hidden="true" />
        </Button>
        <Button
          data-workbench-test
          type="button"
          variant="outline"
          size="sm"
          :disabled="!canWrite || testing.isPending.value || stepCount === 0"
          @click="toggleRun"
        >
          <Play aria-hidden="true" />
          {{ lastRun ? $t(`${page}.run.clear`) : $t(`${page}.run.start`) }}
        </Button>
        <Button
          data-workbench-save
          type="button"
          size="sm"
          :disabled="!canWrite || !draft.dirty.value || saving.isPending.value"
          @click="save"
        >
          {{ $t('dashboard.pipelines.workbench.save') }}
        </Button>
        <PipelineMenu
          v-if="pipeline.data.value"
          :channel-id="channelId"
          :pipeline="pipeline.data.value"
          :can-write="canWrite"
          :sources="sources"
          @deleted="leaveDeleted"
        />
      </span>
    </template>
    <template #bar><PageTabs :tabs="views" /></template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 flex-1 gap-3 p-6 max-md:py-0">
    <SidePanel
      v-model:open="paletteOpen"
      side="left"
      pinned
      :label="$t('dashboard.pipelines.workbench.palette.label')"
    >
      <StepPalette
        ref="palette"
        class="xl:w-64"
        :actions="catalog.data.value?.actions ?? []"
        :read-only="!canWrite"
        :target="target?.label ?? null"
        @action="addAction"
        @block="addBlock"
        @release="target = null"
      />
    </SidePanel>
    <RunsPanel v-if="view === 'runs'" :channel-id="channelId" :pipeline-id="id" />
    <PanelCard
      v-else
      class="min-w-0 flex-1"
      :title="$t('dashboard.pipelines.workbench.steps', { count: stepCount }, stepCount)"
      :state="state"
      :error-text="$t('dashboard.pipelines.workbench.error')"
      @retry="pipeline.refetch()"
    >
      <template v-if="summary" #actions>
        <span data-run-summary class="flex flex-wrap items-center justify-end gap-2">
          <StatusBadge
            :tone="summary.failed > 0 ? 'bad' : 'ok'"
            :label="$t(`${page}.run.summary`, { number: runCount, ran: summary.ran }, summary.ran)"
            class="text-xs"
          />
          <TestRunInput v-model="runInput" />
          <Button
            data-run-again
            type="button"
            variant="ghost"
            size="sm"
            :disabled="testing.isPending.value"
            @click="testRun"
          >
            <Play aria-hidden="true" />
            {{ $t(`${page}.run.again`) }}
          </Button>
        </span>
      </template>
      <div data-workbench-canvas class="mx-auto flex w-full max-w-3xl flex-col gap-3 p-4">
        <p data-workbench-start class="flex w-fit items-center gap-2 rounded-full border bg-muted px-3 py-1.5 text-sm">
          <Lightning class="size-4 shrink-0" aria-hidden="true" />
          {{ starts }}
        </p>
        <ol v-if="tree.length > 0" class="flex flex-col gap-2">
          <StepCard
            v-for="node in tree"
            :key="node.key"
            :node="node"
            :selected-key="selectedKey"
            :descriptors="descriptors"
            :read-only="!canWrite"
            :marks="marks"
            @select="pick"
            @add-to="addTo"
          />
        </ol>
        <p v-else class="text-sm text-muted-foreground">
          {{ $t('dashboard.pipelines.workbench.empty') }}
        </p>
        <Button
          data-workbench-add
          type="button"
          variant="outline"
          size="sm"
          class="w-fit"
          :disabled="!canWrite"
          :title="canWrite ? undefined : $t('dashboard.pipelines.noWrite')"
          @click="openPalette"
        >
          <Add aria-hidden="true" />
          {{ $t('dashboard.pipelines.workbench.palette.addEnd') }}
        </Button>
        <p class="flex w-fit items-center gap-2 rounded-full border bg-muted px-3 py-1.5 text-sm">
          <Flag class="size-4 shrink-0" aria-hidden="true" />
          {{ $t('dashboard.pipelines.workbench.end') }}
        </p>
      </div>
    </PanelCard>
    <SidePanel v-model:open="inspecting" :label="$t('dashboard.pipelines.workbench.inspector.label')">
      <StepInspector
        v-if="selected"
        :step="selected.step"
        :number="selected.number"
        :descriptor="descriptors.get(selected.step.action.type)"
        :read-only="!canWrite"
        @duplicate="duplicate"
        @remove="remove"
        @patch="patch"
      />
    </SidePanel>
  </div>
  <ConfirmDialog
    v-model:open="leaveOpen"
    :title="$t('dashboard.pipelines.workbench.leave.title')"
    :description="$t('dashboard.pipelines.workbench.leave.description')"
    :action="$t('dashboard.pipelines.workbench.leave.action')"
    @confirm="leave"
  />
</template>
