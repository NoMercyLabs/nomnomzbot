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
import ClockBackward from '~icons/nnz/clock-backward';
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
  type PipelineStep,
  type PipelineTestRun,
} from '@/api/pipelines';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useScreenFrom } from '@/composables/useLayout';
import { usePageView } from '@/composables/usePageView';
import { errorText } from '@/lib/errors';
import { usePanelState } from '@/composables/usePanelState';
import { useShortcut } from '@/composables/useShortcut';

import { newAction, newBlock, type AddableBlock } from './components/newSteps';
import PipelineDeleteButton from './components/PipelineDeleteButton.vue';
import PipelinePowerSwitch from './components/PipelinePowerSwitch.vue';
import RunsPanel from './components/RunsPanel.vue';
import StepCard from './components/StepCard.vue';
import StepInspector from './components/StepInspector.vue';
import { laneEnd, type MintId, type Spot, type StepPatch } from './components/stepEdits';
import StepPalette from './components/StepPalette.vue';
import { runMarks, runSummary } from './components/stepRun';
import TestRunInput from './components/TestRunInput.vue';
import { buildStepTree, findNode, findStepNode, type StepLane } from './components/stepTree';
import { startSources } from './components/stepWords';
import { useStepDraft } from './useStepDraft';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const { channelId } = useCurrentChannel();
const id = computed(() => String(route.params.id));
const page = 'dashboard.pipelines.workbench';

// Runs lists what the pipeline did when it really ran, in a side panel beside the steps (owner 2026-10-17:
// "can runs not be a side pannel?"). The address keeps ?view=runs, so a link opens it.
const VIEWS = ['steps', 'runs'] as const satisfies readonly string[];
const view = usePageView(VIEWS);
const runsOpen = computed<boolean>({
  get: () => view.value === 'runs',
  set: (open: boolean) => {
    if (open === (view.value === 'runs')) return;

    view.value = open
      ? 'runs'
      : 'steps';
  },
});

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

const descriptors = computed(
  () =>
    new Map(
      (catalog.data.value?.actions ?? []).map((action) => [
        action.type,
        action,
      ]),
    ),
);

const sources = computed(() => startSources(radius.data.value));

const starts = computed(() => {
  if (sources.value.length === 0)
    return t('dashboard.pipelines.workbench.starts.none');

  const named = sources.value.map((source) =>
    t(`dashboard.pipelines.workbench.starts.${source.kind}`, { name: source.name }),
  );

  return t('dashboard.pipelines.workbench.starts.sentence', { sources: named.join(', ') });
});

const selectedKey = ref<string | null>(null);
const selected = computed(() =>
  selectedKey.value === null
    ? undefined
    : findNode(tree.value, selectedKey.value),
);

// The drawer closes by back, a tap outside or Escape; closing it lets go of the step.
const inspecting = computed<boolean>({
  get: () => selected.value !== undefined,
  set: (open: boolean) => {
    if (!open) {
      selectedKey.value = null;
    }
  },
});

// The open step lives in the address (?step=<step id>), so a page that sends the person away for one step's
// sake (the script editor) can bring them back to it. The address is read once, when the steps first arrive.
let stepRestored = false;

watch(
  tree,
  (nodes) => {
    const asked = route.query.step;

    if (stepRestored || nodes.length === 0) return;

    stepRestored = true;

    if (typeof asked === 'string') {
      selectedKey.value = findStepNode(nodes, asked)?.key ?? null;
    }
  },
  { immediate: true },
);

watch(
  () => selected.value?.step.id,
  (stepId) => {
    if (!stepRestored || route.query.step === (stepId ?? undefined)) return;

    const { step, ...rest } = route.query;

    void step;

    void router.replace({ query: stepId
      ? { ...rest, step: stepId }
      : rest });
  },
);

// Runs and the step details share the right side, so one opening closes the other.
watch(runsOpen, (open: boolean) => {
  if (open) {
    selectedKey.value = null;
  }
});

function pick(key: string): void {
  runsOpen.value = false;

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

// The palette (256px) and the details (480px) leave the steps about 215px on a laptop. While a step is open
// the palette waits in its drawer, until the window holds three columns.
const threeColumns = useScreenFrom('threePanels');
const paletteBeside = computed<boolean>(() =>
  !inspecting.value || threeColumns.value);
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
  const make = (mint: MintId): PipelineStep[] => newAction(type, mint);
  const id = draft.add(make, spot());

  added(id);
}

function addBlock(kind: AddableBlock): void {
  const make = (mint: MintId): PipelineStep[] => newBlock(kind, mint);
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

useShortcut(
  {
    key: 'z',
    mod: true,
    label: `${page}.shortcuts.undo`,
  },
  (event: KeyboardEvent) => {
    if (event.shiftKey) {
      draft.redo();
    } else {
      draft.undo();
    }
  },
);

useShortcut(
  {
    key: 'y',
    mod: true,
    label: `${page}.shortcuts.redo`,
  },
  draft.redo,
);

useShortcut(
  {
    key: 'a',
    label: `${page}.shortcuts.add`,
  },
  () => {
    if (canWrite.value) {
      void openPalette();
    }
  },
);

useShortcut(
  {
    key: 's',
    mod: true,
    label: `${page}.shortcuts.save`,
    inFields: true,
  },
  () => {
    void save();
  },
);

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
  <PageHeader
    :title="pipeline.data.value?.name ?? $t('dashboard.pipelines.title')"
    :crumb="$t('dashboard.pipelines.title')"
    :back="{ name: 'dashboard-pipelines' }"
    :back-label="$t('dashboard.pipelines.back')"
  >
    <template v-if="pipeline.data.value && !pipeline.data.value.isEnabled" #status>
      <StatusBadge tone="off" :label="$t('dashboard.pipelines.workbench.off')" />
    </template>
    <!-- The editing controls act on the steps, so they sit in the bar beside the tabs, not in the title row. -->
    <template #bar>
      <Button
        v-if="access.can('pipelines:read')"
        data-workbench-runs
        type="button"
        variant="ghost"
        size="sm"
        :aria-pressed="runsOpen"
        @click="runsOpen = !runsOpen"
      >
        <ClockBackward aria-hidden="true" />
        {{ $t(`${page}.views.runs`) }}
      </Button>
      <span
        v-if="canWrite"
        data-workbench-controls
        class="ml-auto flex shrink-0 items-center gap-1"
      >
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
          {{ $t("dashboard.pipelines.workbench.save") }}
        </Button>
      </span>
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 flex-1 gap-3 p-6 max-md:py-0">
    <SidePanel
      v-if="canWrite"
      v-model:open="paletteOpen"
      side="left"
      :pinned="paletteBeside"
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
    <PanelCard
      v-if="access.can('pipelines:read')"
      class="min-w-0 flex-1"
      :title="$t('dashboard.pipelines.workbench.steps', { count: stepCount }, stepCount)"
      :state="state"
      :error-text="$t('dashboard.pipelines.workbench.error')"
      @retry="pipeline.refetch()"
    >
      <!-- Like every editor: the Enabled switch in the card header, Delete apart at the bottom. -->
      <template #actions>
        <span v-if="summary" data-run-summary class="flex flex-wrap items-center justify-end gap-2">
          <StatusBadge
            :tone="summary.failed > 0 ? 'bad' : 'ok'"
            :label="$t(`${page}.run.summary`, { number: runCount, ran: summary.ran }, summary.ran)"
            class="text-xs"
          />
          <TestRunInput v-model="runInput" />
          <Button
            v-if="canWrite"
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
        <PipelinePowerSwitch
          v-if="pipeline.data.value"
          :channel-id="channelId"
          :pipeline="pipeline.data.value"
          :can-write="canWrite"
        />
      </template>
      <div data-workbench-canvas class="mx-auto flex w-full max-w-3xl flex-col gap-3 p-4">
        <p
          data-workbench-start
          class="flex w-fit items-center gap-2 rounded-full border bg-muted px-3 py-1.5 text-sm"
        >
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
          {{ $t("dashboard.pipelines.workbench.empty") }}
        </p>
        <Button
          v-if="canWrite"
          data-workbench-add
          type="button"
          variant="outline"
          size="sm"
          class="w-fit"
          @click="openPalette"
        >
          <Add aria-hidden="true" />
          {{ $t("dashboard.pipelines.workbench.palette.addEnd") }}
        </Button>
        <p class="flex w-fit items-center gap-2 rounded-full border bg-muted px-3 py-1.5 text-sm">
          <Flag class="size-4 shrink-0" aria-hidden="true" />
          {{ $t("dashboard.pipelines.workbench.end") }}
        </p>
      </div>
      <template v-if="pipeline.data.value" #footer>
        <PipelineDeleteButton
          :channel-id="channelId"
          :pipeline="pipeline.data.value"
          :can-write="canWrite"
          :sources="sources"
          @deleted="leaveDeleted"
        />
      </template>
    </PanelCard>
    <SidePanel
      v-model:open="inspecting"
      :label="$t('dashboard.pipelines.workbench.inspector.label')"
    >
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
    <SidePanel
      v-if="access.can('pipelines:read')"
      v-model:open="runsOpen"
      :label="$t(`${page}.runs.title`)"
    >
      <RunsPanel :channel-id="channelId" :pipeline-id="id" />
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
