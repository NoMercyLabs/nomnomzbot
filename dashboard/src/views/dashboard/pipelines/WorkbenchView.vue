<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
import { computed, nextTick, ref, shallowRef, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router';

import { useChannelAccess } from '@/api/access';
import { ApiError } from '@/api/errors';
import {
  useBlastRadius,
  usePipeline,
  usePipelineCatalog,
  useSavePipeline,
  useTestRunPipeline,
  type PipelineStep,
  type PipelineTestRun,
  type PipelineInput,
} from '@/api/pipelines';
import type { JsonValue } from '@/lib/json';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import DateTime from '@/components/layout/DateTime.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useDraftStorage } from '@/composables/useDraftStorage';
import { useScreenFrom } from '@/composables/useLayout';
import { usePageView } from '@/composables/usePageView';
import { errorText } from '@/lib/errors';
import { usePanelState } from '@/composables/usePanelState';
import { useQueryPane } from '@/composables/useQueryPane';
import { useShortcut } from '@/composables/useShortcut';
import { merge } from '@/lib/array';

import { newAction, newBlock, type AddableBlock } from './components/newSteps';
import LiveRunConfirm from './components/LiveRunConfirm.vue';
import LiveRunResult from './components/LiveRunResult.vue';
import PipelineDeleteButton from './components/PipelineDeleteButton.vue';
import PipelinePowerSwitch from './components/PipelinePowerSwitch.vue';
import PipelineInputsEditor from './components/PipelineInputsEditor.vue';
import RunsPanel from './components/RunsPanel.vue';
import StepCard from './components/StepCard.vue';
import { declaredVariables, earlierTexts } from './components/stepChain';
import StepInspector from './components/StepInspector.vue';
import { laneEnd, type MintId, type Spot, type StepPatch } from './components/stepEdits';
import StepPalette from './components/StepPalette.vue';
import { runMarks, runSummary } from './components/stepRun';
import TestRunInput from './components/TestRunInput.vue';
import {
  buildStepTree,
  findNode,
  findStepNode,
  keyboardSpot,
  locateStep,
  type MoveDirection,
  type StepLane,
} from './components/stepTree';
import { startSources } from './components/stepWords';
import { vLaneDrag } from './components/useStepDrag';
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
const inputDraft = shallowRef<PipelineInput[]>([]);
const savedInputs = ref('[]');

watch(() => pipeline.data.value?.inputs, (inputs) => {
  if (!inputs) return;

  inputDraft.value = inputs.map((input) => ({ ...input }));
  savedInputs.value = JSON.stringify(inputs);
}, { immediate: true });

const inputDirty = computed(() => JSON.stringify(inputDraft.value) !== savedInputs.value);
const dirty = computed(() => draft.dirty.value || inputDirty.value);

// Unsaved steps survive a reload in this browser, one entry per channel and pipeline.
const stored = useDraftStorage<PipelineStep[]>(
  () =>
    channelId.value && `${channelId.value}.${id.value}`,
  draft.steps,
  draft.dirty,
  () =>
    pipeline.data.value?.updatedAt,
);

function restoreDraft(): void {
  const restored = stored.restore();

  if (restored) {
    draft.replace(restored);
  }
}

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

// The open step lives in the address (?step=<step id>) as a pane: opening it is one history step, so Back closes
// it, and a page that sends the person away for one step's sake (the script editor) can bring them back to it.
// The drawer is a layer, so the pane and the layer share that step.
const pane = useQueryPane('step', [], { inLayer: true });
const selected = computed(() => {
  const open = pane.id.value;

  return open === null
    ? undefined
    : findStepNode(tree.value, open);
});
const selectedKey = computed(() => selected.value?.key ?? null);

// The drawer closes by back, a tap outside or Escape; closing it lets go of the step.
const inspecting = computed<boolean>({
  get: () => selected.value !== undefined,
  set: (open: boolean) => {
    if (!open) {
      pane.close();
    }
  },
});

// Runs and the step details share the right side, so one opening closes the other.
watch(runsOpen, (open: boolean) => {
  if (open && pane.id.value !== null) {
    pane.close();
  }
});

function pick(key: string): void {
  runsOpen.value = false;

  const step = findNode(tree.value, key)?.step;

  pane.select(key === selectedKey.value
    ? null
    : step?.id);
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

// Cards are keyed by their place, so after a move the open step and the focus follow the step by its id.
const moved = ref('');

async function moveTo(stepId: string, to: Spot): Promise<void> {
  draft.move(stepId, to);

  const place = locateStep(tree.value, stepId);

  moved.value = place
    ? t(`${page}.drag.moved`, place)
    : '';

  await nextTick();
  document.querySelector<HTMLElement>(`[data-step-id="${stepId}"] [data-step-handle]`)?.focus();
}

function nudge(stepId: string, direction: MoveDirection): void {
  const to = keyboardSpot(tree.value, stepId, direction);

  if (to) {
    void moveTo(stepId, to);
  }
}

function remove(): void {
  const step = selected.value?.step;

  if (!step?.id) return;

  pane.close();
  draft.remove(step.id);
  toast(t(`${page}.removed`), { action: { label: t(`${page}.undo`), onClick: draft.undo } });
}

const paletteOpen = ref(false);

// The steps never move when a step opens (Stoney 2026-10-20: "the pipeline editor needs to not layout shift
// when toggeling the change pannel"): the details keep their column while no step is open, and the palette
// (256px) sits beside only on a window that holds all three columns; on a laptop it waits in its drawer.
const paletteBeside = useScreenFrom('threePanels');
const wide = useScreenFrom('wide');

// The text earlier steps hold, so the open step can use the same words instead of a typed copy.
const earlier = computed(() =>
  selectedKey.value === null
    ? []
    : earlierTexts(tree.value, selectedKey.value, descriptors.value));
// The variables earlier steps write; the open step's template fields accept them as known names.
const inputNames = computed(() => inputDraft.value.map((input) => input.name).filter(Boolean));
const variables = computed(() => {
  if (selectedKey.value === null)
    return [];

  const declared = declaredVariables(tree.value, selectedKey.value, descriptors.value);

  return Array.from(merge(inputNames.value, declared));
});
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
  pane.select(node?.step.id);
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
const runValues = ref<Record<string, JsonValue>>({});
const testRunInputs = useTemplateRef<InstanceType<typeof TestRunInput>>('testRunInputs');
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
  liveError.value = null;
});

// A live run runs the real actions, so it asks first. Dry stays the default, and nothing remembers a live choice.
const RUN_MODES = ['dry', 'live'] as const satisfies readonly string[];

type RunMode = (typeof RUN_MODES)[number];

const runMode = ref<RunMode>('dry');
const liveOpen = ref(false);
const liveError = ref<string | null>(null);
const modeOptions = computed(() =>
  RUN_MODES.map((value) =>
    ({ value, label: t(`${page}.run.mode.${value}`) })));

const testLabel = computed(() => {
  if (lastRun.value !== null)
    return t(`${page}.run.clear`);

  return runMode.value === 'live'
    ? t(`${page}.run.startLive`)
    : t(`${page}.run.start`);
});

// 409: one live run of this pipeline is still going. 403: the person may not run it live.
const LIVE_ERRORS = {
  409: 'inProgress',
  403: 'forbidden',
} as const satisfies Record<number, string>;

function liveErrorOf(error: Error): string | null {
  const key = error instanceof ApiError && error.status !== undefined
    ? (LIVE_ERRORS as Record<number, string | undefined>)[error.status]
    : undefined;

  return key === undefined
    ? null
    : t(`${page}.live.${key}`);
}

async function testRun(): Promise<void> {
  liveError.value = null;
  liveOpen.value = false;

  try {
    lastRun.value = await testing.mutateAsync({
      id: id.value,
      variables: runInput.value,
      ...(Object.keys(runValues.value).length > 0
        ? { inputs: runValues.value }
        : {}),
      ...(inputDirty.value
        ? { inputDefinitions: inputDraft.value }
        : {}),
      graph: { steps: draft.steps.value },
      mode: runMode.value === 'live'
        ? 'live'
        : undefined,
    });

    runCount.value += 1;
  } catch (error: unknown) {
    const line = runMode.value === 'live' && error instanceof Error
      ? liveErrorOf(error)
      : null;

    if (line !== null) {
      liveError.value = line;

      return;
    }

    toast.error(error instanceof Error
      ? errorText(error, t)
      : t(`${page}.run.failedToStart`));
  }
}

function continueRun(): void {
  if (runMode.value === 'live') {
    liveOpen.value = true;
  } else {
    void testRun();
  }
}

function startRun(): void {
  if (inputDraft.value.length > 0) {
    testRunInputs.value?.show();

    return;
  }

  continueRun();
}

function toggleRun(): void {
  if (lastRun.value === null) {
    startRun();
  } else {
    lastRun.value = null;
  }
}

async function save(): Promise<void> {
  // A field hands its typed value over when it is left, so Ctrl+S in a field leaves it first.
  if (document.activeElement instanceof HTMLElement) {
    document.activeElement.blur();
  }

  if (!dirty.value || !canWrite.value) return;

  try {
    await saving.mutateAsync({
      id: id.value,
      ...(draft.dirty.value
        ? { graph: { steps: draft.steps.value } }
        : {}),
      ...(inputDirty.value
        ? { inputs: inputDraft.value }
        : {}),
    });

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
  if (leaveAnyway || !dirty.value)
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
        <SegmentedControl
          v-if="canWrite"
          v-model="runMode"
          data-run-mode
          :label="$t(`${page}.run.mode.label`)"
          :options="modeOptions"
          :disabled="testing.isPending.value"
        />
        <TestRunInput ref="testRunInputs"
                      v-model="runInput"
                      v-model:inputs="runValues"
                      :definitions="inputDraft"
                      @confirm="continueRun"
        />
        <!-- A live run has real effects, so its button takes the destructive variant, never the accent. -->
        <Button
          data-workbench-test
          type="button"
          :variant="runMode === 'live' ? 'destructive' : 'outline'"
          size="sm"
          :disabled="!canWrite || testing.isPending.value || stepCount === 0"
          @click="toggleRun"
        >
          <Play aria-hidden="true" />
          {{ testLabel }}
        </Button>
        <Button
          data-workbench-save
          type="button"
          size="sm"
          :disabled="!canWrite || !dirty || saving.isPending.value"
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
          <Button
            v-if="canWrite"
            data-run-again
            type="button"
            variant="ghost"
            size="sm"
            :disabled="testing.isPending.value"
            @click="startRun"
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
        <!-- One primary action (Restore); Discard is outline. The banner is a tinted card, not accent-filled. -->
        <div
          v-if="canWrite && stored.stored.value"
          data-draft-banner
          role="status"
          class="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-muted px-4 py-3 text-sm"
        >
          <p class="min-w-0 flex-1">
            <i18n-t keypath="dashboard.pipelines.workbench.draft.message" scope="global">
              <template #time>
                <DateTime :value="stored.stored.value.savedAt" variant="dateTime" />
              </template>
            </i18n-t>
            <span v-if="stored.stale.value" data-draft-stale class="block text-muted-foreground">
              {{ $t('dashboard.pipelines.workbench.draft.stale') }}
            </span>
          </p>
          <span class="flex shrink-0 items-center gap-2">
            <Button data-draft-discard
                    type="button"
                    variant="outline"
                    size="sm"
                    @click="stored.discard()"
            >
              {{ $t('dashboard.pipelines.workbench.draft.discard') }}
            </Button>
            <Button data-draft-restore type="button" size="sm" @click="restoreDraft">
              {{ $t('dashboard.pipelines.workbench.draft.restore') }}
            </Button>
          </span>
        </div>
        <p
          v-if="liveError"
          data-live-error
          role="alert"
          class="m-0 rounded-lg border border-destructive/50 px-4 py-3 text-sm text-destructive"
        >
          {{ liveError }}
        </p>
        <LiveRunResult v-if="lastRun?.mode === 'live'" :run="lastRun" />
        <PipelineInputsEditor v-model="inputDraft" :read-only="!canWrite" />
        <p
          data-workbench-start
          class="flex w-fit items-center gap-2 rounded-full border bg-muted px-3 py-1.5 text-sm"
        >
          <Lightning class="size-4 shrink-0" aria-hidden="true" />
          {{ starts }}
        </p>
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
            :marks="marks"
            @select="pick"
            @add-to="addTo"
            @move="moveTo"
            @nudge="nudge"
          />
        </ol>
        <p v-else class="text-sm text-muted-foreground">
          {{ $t("dashboard.pipelines.workbench.empty") }}
        </p>
        <!-- Tells a screen reader where a keyboard move put the step. -->
        <p data-move-announcer class="sr-only" role="status" aria-live="polite">{{ moved }}</p>
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
        :earlier="earlier"
        :variables="variables"
        @duplicate="duplicate"
        @remove="remove"
        @patch="patch"
      />
    </SidePanel>
    <!-- Holds the details column while no step is open, so opening one never moves the steps. -->
    <aside
      v-if="wide && !inspecting && !runsOpen"
      data-step-inspector-empty
      class="flex w-120 flex-none flex-col *:flex-1"
    >
      <PanelCard :title="$t('dashboard.pipelines.workbench.inspector.label')" pad>
        <p class="text-sm text-muted-foreground">
          {{ $t("dashboard.pipelines.workbench.inspector.pick") }}
        </p>
      </PanelCard>
    </aside>
    <SidePanel
      v-if="access.can('pipelines:read')"
      v-model:open="runsOpen"
      :label="$t(`${page}.runs.title`)"
    >
      <RunsPanel :channel-id="channelId" :pipeline-id="id" />
    </SidePanel>
  </div>
  <LiveRunConfirm
    v-if="canWrite"
    v-model:open="liveOpen"
    :steps="draft.steps.value"
    :descriptors="descriptors"
    @confirm="testRun"
  />
  <ConfirmDialog
    v-model:open="leaveOpen"
    :title="$t('dashboard.pipelines.workbench.leave.title')"
    :description="$t('dashboard.pipelines.workbench.leave.description')"
    :action="$t('dashboard.pipelines.workbench.leave.action')"
    @confirm="leave"
  />
</template>
