<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One pipeline on the workbench (5-workbench.html): what starts it, its steps in the order the engine walks
// them with every check split into lanes, and the picked step in detail beside the canvas. Narrower than a
// laptop the details slide in as a drawer, and the back button closes it.
import Flag from '~icons/nnz/flag';
import Lightning from '~icons/nnz/lightning';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';

import {
  useBlastRadius,
  usePipeline,
  usePipelineCatalog,
  type PipelineActionDescriptor,
} from '@/api/pipelines';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import StepCard from './components/StepCard.vue';
import StepInspector from './components/StepInspector.vue';
import { buildStepTree, findNode } from './components/stepTree';
import { startSources, type StartSource } from './components/stepWords';

const { t } = useI18n();
const route = useRoute();
const { channelId } = useCurrentChannel();
const id = computed(() => String(route.params.id));

const pipeline = usePipeline(channelId, id);
const catalog = usePipelineCatalog(channelId);
const radius = useBlastRadius(channelId, id);

const state = usePanelState(pipeline);
const tree = computed(() => buildStepTree(pipeline.data.value?.graph.steps ?? []));
const stepCount = computed(() => pipeline.data.value?.graph.steps.length ?? 0);

const descriptors = computed(() =>
  new Map((catalog.data.value?.actions ?? []).map((action: PipelineActionDescriptor) =>
    [action.type, action])));

const starts = computed(() => {
  const sources = startSources(radius.data.value);

  if (sources.length === 0)
    return t('dashboard.pipelines.workbench.starts.none');

  const named = sources.map((source: StartSource) =>
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
</script>

<template>
  <PageHeader :title="pipeline.data.value?.name ?? $t('dashboard.pipelines.title')" :crumb="$t('dashboard.pipelines.title')">
    <template v-if="pipeline.data.value && !pipeline.data.value.isEnabled" #status>
      <StatusBadge tone="off" :label="$t('dashboard.pipelines.workbench.off')" />
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 flex-1 gap-3 p-6 max-md:py-0">
    <PanelCard
      class="min-w-0 flex-1"
      :title="$t('dashboard.pipelines.workbench.steps', { count: stepCount }, stepCount)"
      :state="state"
      :error-text="$t('dashboard.pipelines.workbench.error')"
      @retry="pipeline.refetch()"
    >
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
            @select="pick"
          />
        </ol>
        <p v-else class="text-sm text-muted-foreground">
          {{ $t('dashboard.pipelines.workbench.empty') }}
        </p>
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
      />
    </SidePanel>
  </div>
</template>
