<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Why one run ended the way it did, as a sheet from the right: how it ended, the limit that stopped it, who
// and what triggered it, then its steps and the host calls inside each step. The run id is in the address
// (?run=), so a reload or a copied link reopens the same run. Copy link is the one action here; the status
// dot is the only red until a step or a call failed.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';
import Cross from '~icons/nnz/cross';

import { useChannelRun, type ChannelRunDetail } from '@/api/adminDiagnostics';
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Sheet, SheetClose, SheetContent, SheetDescription, SheetTitle } from '@/components/ui/sheet';
import { useTime } from '@/composables/useTime';
import { usePanelState } from '@/composables/usePanelState';
import { durationText } from '@/lib/time';

import RunFailureKind from './RunFailureKind.vue';
import RunStatus from './RunStatus.vue';
import RunStepTimeline from './RunStepTimeline.vue';

const props = defineProps<{ channelId: string; runId: number | null }>();

defineEmits<{ close: [] }>();

const { t, locale } = useI18n();
const route = useRoute();
const time = useTime();

const read = useChannelRun(() => props.channelId, () => props.runId);
const run = computed<ChannelRunDetail | null>(() => read.data.value ?? null);
const state = usePanelState(read, (data) =>
  data === null);

const link = computed<string>(() => `${window.location.origin}${route.fullPath}`);

const limit = computed<string>(() => {
  const detail = run.value;

  return detail?.limitKey
    ? t('dashboard.admin.console.runs.detail.limit', { key: detail.limitKey, value: String(detail.limitValue ?? '') })
    : '';
});

const facts = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.admin.console.runs.columns.pipeline'), value: run.value?.pipelineId },
  { label: t('dashboard.admin.console.runs.columns.trigger'), value: run.value?.triggerKind },
  { label: t('dashboard.admin.console.runs.columns.person'), value: run.value?.triggeredByUserId },
  { label: t('dashboard.admin.console.runs.detail.started'), value: time.dateTime(run.value?.startedAt) },
  { label: t('dashboard.admin.console.runs.columns.duration'), value: durationText(Number(run.value?.durationMs ?? 0), locale.value) },
  { label: t('dashboard.admin.console.runs.columns.error'), value: run.value?.errorMessage },
]);
</script>

<template>
  <Sheet :open="runId !== null" @update:open="(open: boolean) => !open && $emit('close')">
    <SheetContent
      data-run-detail
      :show-close-button="false"
      class="gap-0 p-0 data-[side=right]:w-120 data-[side=right]:max-w-[90vw] data-[side=right]:sm:max-w-[90vw] *:flex-1 *:rounded-none *:border-0"
    >
      <SheetTitle class="sr-only">{{ t('dashboard.admin.console.runs.detail.title', { id: runId }) }}</SheetTitle>
      <SheetDescription class="sr-only">{{ t('dashboard.admin.console.runs.detail.description') }}</SheetDescription>
      <PanelCard
        flush
        pad
        :title="t('dashboard.admin.console.runs.detail.title', { id: runId })"
        :description="t('dashboard.admin.console.runs.detail.description')"
        :state="state"
        :empty-text="t('dashboard.admin.console.runs.detail.error')"
        :error-text="t('dashboard.admin.console.runs.detail.error')"
        @retry="read.refetch()"
      >
        <template #actions>
          <SheetClose as-child>
            <Button
              data-run-close
              variant="ghost"
              size="icon"
              :aria-label="t('common.close')"
            >
              <Cross class="size-5" aria-hidden="true" />
            </Button>
          </SheetClose>
        </template>
        <div v-if="run" class="grid gap-4">
          <div class="flex flex-wrap items-center gap-3">
            <RunStatus :status="run.status" />
            <RunFailureKind :kind="run.failureKind" />
          </div>
          <p v-if="limit" data-run-limit class="text-sm font-medium">{{ limit }}</p>
          <KeyValueList :items="facts" />
          <section class="grid gap-2">
            <h3 class="text-sm font-semibold">{{ t('dashboard.admin.console.runs.detail.steps') }}</h3>
            <RunStepTimeline
              v-if="run.stepLogs.length > 0"
              :steps="run.stepLogs"
              :run-ms="Number(run.durationMs)"
            />
            <p v-else class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.runs.detail.noSteps') }}</p>
          </section>
        </div>
        <template #footer>
          <CopyLinkButton :url="link" :label="t('dashboard.admin.console.copyLink')" />
        </template>
      </PanelCard>
    </SheetContent>
  </Sheet>
</template>
