<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Every pipeline of the channel: a search box and one row per pipeline. A row opens its workbench.
import Plus from '~icons/nnz/add';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';

import { useChannelAccess } from '@/api/access';
import { usePipelines, type PipelineListItem } from '@/api/pipelines';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
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
    .filter((row: PipelineListItem) =>
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
      <Button
        v-if="canWrite"
        data-pipelines-new
        type="button"
        variant="outline"
        size="sm"
        @click="creating = true"
      >
        <Plus aria-hidden="true" />
        {{ $t("dashboard.pipelines.new") }}
      </Button>
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 flex-1 flex-col gap-3 p-6 max-md:pt-3 max-md:pb-0">
    <PanelCard
      v-if="access.can('pipelines:read')"
      :title="$t('dashboard.pipelines.list.title')"
      :state="state"
      :error-text="$t('dashboard.pipelines.list.error')"
      @retry="pipelines.refetch()"
    >
      <template #actions>
        <!-- Concentric with the panel corner: 8px from the top and the right, so a 20 - 8 = 12px radius. -->
        <SearchField
          v-model="search"
          class="-mr-2 w-52 rounded-[12px]"
          :placeholder="$t('dashboard.pipelines.list.search')"
        />
      </template>
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
        :title="row.name"
        :detail="row.description || detailOf(row)"
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
    </PanelCard>
  </div>
  <NewPipelineDialog v-if="canWrite" v-model:open="creating" @created="open" />
</template>
