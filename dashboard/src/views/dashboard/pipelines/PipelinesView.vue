<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Every pipeline of the channel: a search box and one row per pipeline. A row opens its workbench.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';

import { useChannelAccess } from '@/api/access';
import { usePipelines, type PipelineListItem } from '@/api/pipelines';
import ListPanel from '@/components/layout/ListPanel.vue';
import PageCreateButton from '@/components/layout/PageCreateButton.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import type { PanelState } from '@/composables/usePanelState';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { agoText } from '@/lib/time';

import NewPipelineDialog from './components/NewPipelineDialog.vue';

const { t, locale } = useI18n();
const router = useRouter();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
const pipelines = usePipelines(channelId);

const canWrite = computed(() => access.can('pipelines:write'));

const search = ref('');

const panel = usePanelState(pipelines, (data: PipelineListItem[]) =>
  data.length === 0);
// The empty state holds a button, so the panel shows it as ready content.
const state = computed<PanelState>(() =>
  panel.value === 'empty'
    ? 'ready'
    : panel.value);
const none = computed(() => panel.value === 'empty');

const rows = computed(() => {
  const needle = search.value.trim().toLowerCase();

  return (pipelines.data.value ?? [])
    .filter((row) =>
      `${row.name} ${row.description ?? ''}`.toLowerCase().includes(needle),
    )
    .sort((left: PipelineListItem, right: PipelineListItem) => left.name.localeCompare(right.name));
});

function detailOf(row: PipelineListItem): string {
  const runs = Number(row.triggerCount);
  const ran = t('dashboard.pipelines.list.runs', { count: runs }, runs);

  if (!row.lastTriggeredAt)
    return ran;

  return t('dashboard.pipelines.list.ranLast', {
    runs: ran,
    ago: agoText(row.lastTriggeredAt, new Date(), locale.value),
  });
}

function open(id: string | null): void {
  if (id === null) return;

  void router.push({ name: 'dashboard-pipeline', params: { id } });
}

const creating = ref(false);
</script>

<template>
  <PageHeader :title="$t('dashboard.pipelines.title')" :crumb="$t('dashboard.pipelines.crumb')">
    <template #actions>
      <PageCreateButton
        data-pipelines-new
        :label="$t('dashboard.pipelines.new')"
        :disabled-reason="canWrite ? undefined : $t('common.readOnly')"
        @create="creating = true"
      />
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 flex-1 flex-col gap-3 p-6 max-md:overflow-y-auto max-md:p-4 max-md:*:shrink-0">
    <ListPanel
      v-if="access.can('pipelines:read')"
      v-model="search"
      :title="$t('dashboard.pipelines.list.title')"
      :placeholder="$t('dashboard.pipelines.list.search')"
      :state="state"
      :error-text="$t('dashboard.pipelines.list.error')"
      @retry="pipelines.refetch()"
    >
      <div
        v-if="none"
        data-slot="panel-empty"
        class="flex h-full flex-col items-center justify-center gap-3 p-4 text-center text-sm text-muted-foreground"
      >
        {{ $t("dashboard.pipelines.list.empty") }}
        <Button v-if="canWrite" type="button" size="sm" @click="creating = true">
          {{ $t("dashboard.pipelines.list.makeFirst") }}
        </Button>
      </div>
      <p v-else-if="rows.length === 0" class="p-4 text-center text-sm text-muted-foreground">
        {{ $t("dashboard.pipelines.list.noMatch") }}
      </p>
      <SummaryRow
        v-for="row in rows"
        v-else
        :key="row.id"
        pick
        chevron
        :title="row.name"
        :detail="row.description || detailOf(row)"
        class="[&_[data-slot=summary-row-text]>span:nth-child(2)>span]:line-clamp-2"
        data-pipeline-row
        :data-pipeline-id="row.id"
        @click="open(row.id)"
      >
        <template #end>
          <span class="flex items-center gap-2">
            <Badge v-if="row.hasPlatformDefault" variant="secondary">
              {{ $t("dashboard.pipelines.list.readyMade") }}
            </Badge>
            <StatusBadge
              v-if="!row.isEnabled"
              tone="off"
              :label="$t('dashboard.pipelines.list.off')"
            />
          </span>
        </template>
      </SummaryRow>
    </ListPanel>
  </div>
  <NewPipelineDialog v-if="canWrite" v-model:open="creating" @created="open" />
</template>
