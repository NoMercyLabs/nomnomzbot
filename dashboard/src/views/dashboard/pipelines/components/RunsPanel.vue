<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Runs view of the Workbench (mockup 5-workbench #runs-view): the stored runs of this pipeline, newest first.
// A run opens in place to show what each of its steps did; only one is open at a time.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import ChevronDown from '~icons/nnz/chevron-down';

import { usePipelineRuns, type PipelineRun, type PipelineRunPage } from '@/api/pipelineRuns';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { agoText, durationText } from '@/lib/time';

import RunSteps from './RunSteps.vue';

const props = defineProps<{
  channelId: string | null;
  pipelineId: string | null;
}>();

const page = 'dashboard.pipelines.workbench';

// The bot stores one of these four outcomes (PipelineExecution.Status); a refusal is not a failure of the steps.
const TONES = {
  success: 'ok',
  failed: 'bad',
  timeout: 'bad',
  denied: 'off',
} as const satisfies Record<string, StatusTone>;

const { t, te, locale } = useI18n();

const runs = usePipelineRuns(() => props.channelId, () =>
  props.pipelineId);
const open = ref<string | null>(null);

const list = computed<PipelineRun[]>(() =>
  (runs.data.value?.pages ?? []).flatMap((part: PipelineRunPage) => part.runs));

const state = computed<PanelState>(() => {
  if (runs.isPending.value)
    return 'loading';

  if (runs.isError.value)
    return 'error';

  return list.value.length > 0
    ? 'ready'
    : 'empty';
});

function toneOf(run: PipelineRun): StatusTone {
  return TONES[run.status as keyof typeof TONES] ?? 'off';
}

// An outcome the bot adds later shows as the bot names it, never as a missing word.
function statusText(run: PipelineRun): string {
  const key = `${page}.runs.status.${run.status}`;

  return te(key)
    ? t(key)
    : run.status;
}

function whenText(run: PipelineRun): string {
  return t(`${page}.runs.when`, {
    ago: agoText(run.startedAt, new Date(), locale.value),
    length: durationText(Number(run.durationMs), locale.value),
  });
}

function toggle(id: string, isOpen: boolean): void {
  open.value = isOpen
    ? id
    : null;
}
</script>

<template>
  <PanelCard
    data-runs
    class="min-w-0 flex-1"
    :title="$t(`${page}.runs.title`)"
    :description="$t(`${page}.runs.description`)"
    :state="state"
    :empty-text="$t(`${page}.runs.empty`)"
    :error-text="$t(`${page}.runs.error`)"
    @retry="runs.refetch()"
  >
    <ul class="grid gap-2 p-4">
      <li v-for="run in list" :key="String(run.id)">
        <Collapsible
          data-run
          :open="open === String(run.id)"
          class="rounded-md border"
          @update:open="(isOpen: boolean) => toggle(String(run.id), isOpen)"
        >
          <CollapsibleTrigger
            class="group flex w-full items-center gap-3 rounded-md px-3 py-2 text-left hover:bg-muted/50"
          >
            <StatusBadge :tone="toneOf(run)" :label="statusText(run)" class="text-xs" />
            <Badge v-if="run.triggerKind === 'test_run'" data-run-test variant="outline" class="text-xs">
              {{ $t(`${page}.runs.testRun`) }}
            </Badge>
            <span class="grid min-w-0 flex-1">
              <span class="truncate text-sm text-muted-foreground">{{ whenText(run) }}</span>
              <span v-if="run.errorMessage" class="truncate text-xs text-destructive">{{ run.errorMessage }}</span>
            </span>
            <ChevronDown
              aria-hidden="true"
              class="size-4 flex-none transition-transform group-data-[state=open]:rotate-180"
            />
          </CollapsibleTrigger>
          <CollapsibleContent class="border-t px-3 py-2">
            <RunSteps :channel-id="channelId" :run-id="String(run.id)" />
          </CollapsibleContent>
        </Collapsible>
      </li>
    </ul>
    <template v-if="runs.hasNextPage.value" #footer>
      <Button
        data-runs-older
        variant="outline"
        size="sm"
        :disabled="runs.isFetchingNextPage.value"
        @click="runs.fetchNextPage()"
      >
        {{ $t(`${page}.runs.older`) }}
      </Button>
    </template>
  </PanelCard>
</template>
