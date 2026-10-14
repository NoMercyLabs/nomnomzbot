<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The steps of one stored run, read once it opens: each step by its name, whether it worked, how long it took and
// why it failed. The bot logs steps by their place in the run, so the number is that place.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePipelineRun, type PipelineRunStep } from '@/api/pipelineRuns';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Skeleton } from '@/components/ui/skeleton';
import { durationText } from '@/lib/time';

import { actionTitle } from './stepWords';

const props = defineProps<{
  channelId: string | null;
  runId: string;
}>();

const page = 'dashboard.pipelines.workbench';

const words = useI18n();

const run = usePipelineRun(() => props.channelId, () =>
  props.runId);
const steps = computed<PipelineRunStep[]>(() => {
  const logs = [...(run.data.value?.stepLogs ?? [])];

  logs.sort((a: PipelineRunStep, b: PipelineRunStep) => Number(a.stepIndex) - Number(b.stepIndex));

  return logs;
});
</script>

<template>
  <div data-run-steps class="grid gap-2">
    <h4 class="text-xs font-medium text-muted-foreground">{{ $t(`${page}.runs.steps`) }}</h4>
    <Skeleton v-if="run.isPending.value" class="h-12 rounded-md" />
    <p v-else-if="run.isError.value" role="alert" class="text-sm text-destructive">
      {{ $t(`${page}.runs.stepsError`) }}
    </p>
    <p v-else-if="steps.length === 0" class="text-sm text-muted-foreground">{{ $t(`${page}.runs.noSteps`) }}</p>
    <ol v-else class="grid gap-1.5">
      <li
        v-for="step in steps"
        :key="String(step.stepIndex)"
        data-run-step
        class="grid grid-cols-[minmax(0,1fr)_auto] items-start gap-x-3 gap-y-0.5 text-sm"
      >
        <span class="truncate">{{
          $t(`${page}.runs.step`, { number: Number(step.stepIndex) + 1, name: actionTitle(step.actionType, words) })
        }}</span>
        <span class="flex items-center gap-2">
          <span class="text-xs text-muted-foreground tabular-nums">{{
            durationText(Number(step.durationMs), $i18n.locale)
          }}</span>
          <StatusBadge
            :tone="step.succeeded ? 'ok' : 'bad'"
            :label="$t(step.succeeded ? `${page}.run.done` : `${page}.run.failed`)"
            class="text-xs"
          />
        </span>
        <span v-if="step.errorMessage" class="col-span-2 text-xs text-destructive">{{ step.errorMessage }}</span>
      </li>
    </ol>
  </div>
</template>
