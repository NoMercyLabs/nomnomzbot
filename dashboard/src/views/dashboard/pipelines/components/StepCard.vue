<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One step on the canvas (5-workbench.html:58-108). The shape says what it is: a circle runs an action, a
// diamond is a check that splits the flow into lanes. A check's lanes hold their own numbered steps (3.1, 3.2).
import CornerDownRight from '~icons/nnz/arrow-forward-down';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PipelineActionDescriptor } from '@/api/pipelines';
import { useLocalizedText } from '@/composables/useLocalizedText';

import { actionIcon, blockIcon } from './stepIcons';
import type { StepLane, StepNode } from './stepTree';
import {
  conditionSentence,
  laneTitle,
  missingFields,
  stepSentence,
  stepTitle,
} from './stepWords';

const props = defineProps<{
  node: StepNode;
  selectedKey: string | null;
  descriptors: ReadonlyMap<string, PipelineActionDescriptor>;
}>();

const emit = defineEmits<{ select: [key: string] }>();

const { t, te } = useI18n();
const text = useLocalizedText();

const step = computed(() => props.node.step);
const isBlock = computed(() => step.value.block_kind !== null);
const descriptor = computed(() =>
  isBlock.value
    ? undefined
    : props.descriptors.get(step.value.action.type));

const icon = computed(() =>
  step.value.block_kind === null
    ? actionIcon(descriptor.value?.category.key)
    : blockIcon(step.value.block_kind));

const title = computed(() => stepTitle(step.value, { t, te }));
const sentence = computed(() => stepSentence(descriptor.value, text));
const condition = computed(() =>
  step.value.condition
    ? conditionSentence(step.value.condition, { t, te })
    : null);
const missing = computed(() => missingFields(step.value, descriptor.value));
const selected = computed(() => props.node.key === props.selectedKey);

function laneName(lane: StepLane, index: number): string {
  return laneTitle(lane, index, { t, te });
}
</script>

<template>
  <li data-step :data-step-id="step.id" :data-step-number="node.number" class="flex flex-col gap-2">
    <button
      type="button"
      class="flex w-full items-start gap-3 rounded-lg border p-3 text-left transition-colors focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none"
      :class="selected
        ? 'border-ring bg-sidebar-accent text-sidebar-accent-foreground'
        : 'bg-card hover:bg-muted/50'"
      :aria-pressed="selected"
      @click="emit('select', node.key)"
    >
      <span
        class="grid size-8 shrink-0 place-items-center bg-muted"
        :class="isBlock
          ? 'rotate-45 rounded-sm'
          : 'rounded-full'"
        aria-hidden="true"
      >
        <component :is="icon" class="size-4" :class="isBlock && '-rotate-45'" />
      </span>
      <span class="flex min-w-0 flex-1 flex-col gap-0.5">
        <span class="flex items-baseline gap-2">
          <span class="min-w-5 text-xs text-muted-foreground tabular-nums">{{ node.number }}</span>
          <span data-step-title class="font-medium">{{ title }}</span>
        </span>
        <span v-if="sentence" class="text-sm text-muted-foreground">{{ sentence }}</span>
        <span v-if="condition" data-step-condition class="text-xs text-muted-foreground">
          {{ condition }}
        </span>
        <span v-if="missing.length > 0" data-step-missing class="text-xs font-medium text-destructive">
          {{ $t('dashboard.pipelines.workbench.notSetUp', { fields: missing.join(', ') }, missing.length) }}
        </span>
      </span>
    </button>
    <div
      v-for="(lane, index) in node.lanes"
      :key="index"
      data-step-lane
      :data-lane-kind="lane.kind"
      class="ml-4 flex flex-col gap-2 border-l-2 pl-4"
    >
      <span class="w-fit rounded-full bg-muted px-2.5 py-0.5 text-xs font-semibold">
        {{ laneName(lane, index) }}
      </span>
      <ol v-if="lane.nodes.length > 0" class="flex flex-col gap-2">
        <StepCard
          v-for="child in lane.nodes"
          :key="child.key"
          :node="child"
          :selected-key="selectedKey"
          :descriptors="descriptors"
          @select="emit('select', $event)"
        />
      </ol>
      <p v-else class="flex items-center gap-2 text-xs text-muted-foreground">
        <CornerDownRight class="size-4" aria-hidden="true" />
        {{ $t('dashboard.pipelines.workbench.lane.empty') }}
      </p>
    </div>
  </li>
</template>
