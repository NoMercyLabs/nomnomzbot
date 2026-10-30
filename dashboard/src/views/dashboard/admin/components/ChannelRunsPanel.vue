<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Runs tab of a channel console: every command, pipeline, script and timer run of the channel, newest
// first, from the diagnostics `runs` section. The filters, the page and the open run live in the address
// (?failuresOnly=, ?pipeline=, ?page=, ?run=), so a copied link reopens the same view. A filter goes back to
// page 1. A row opens the run's detail as a sheet; opening adds a history entry, so Back closes the sheet.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useChannelDiagnostics, type ChannelRun, type DiagnosticRunsQuery } from '@/api/adminDiagnostics';
import FilterBar from '@/components/layout/FilterBar.vue';
import type { FilterOption } from '@/components/layout/filterOption';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Input } from '@/components/ui/input';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './AdminPager.vue';
import ChannelRunsTable from './ChannelRunsTable.vue';
import RunDetailSheet from './RunDetailSheet.vue';
import { readRunsView, runsQuery, type RunsView } from './runsQuery';

const PAGE_SIZE: number = 25;

const props = defineProps<{ channelId: string }>();

const { t } = useI18n();
const route = useRoute();
const router = useRouter();

const view = computed<RunsView>(() => readRunsView(route.query));

const request = computed(() => ({
  page: view.value.page,
  pageSize: PAGE_SIZE,
  failuresOnly: view.value.failuresOnly,
  pipelineId: view.value.pipelineId,
}) as const satisfies DiagnosticRunsQuery);

const diagnostics = useChannelDiagnostics(() => props.channelId, ['runs'], request);

const rows = computed<ChannelRun[]>(() => diagnostics.data.value?.runs?.items ?? []);
const total = computed(() => Number(diagnostics.data.value?.runs?.totalCount ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

const state = usePanelState(diagnostics, (data) =>
  (data?.runs?.items.length ?? 0) === 0);

const showOptions = computed<FilterOption[]>(() => [
  { value: 'all', label: t('dashboard.admin.console.runs.filters.all') },
  { value: 'failures', label: t('dashboard.admin.console.runs.filters.failures') },
]);

const show = computed<string>({
  get: () =>
    view.value.failuresOnly
      ? 'failures'
      : 'all',
  set: (value) => change({ failuresOnly: value === 'failures', page: 1 }),
});

// The field holds what is typed; the address and the request change when the person leaves the field or presses
// Enter, so a long id never sends a read per key.
const pipeline = ref(view.value.pipelineId);

watch(() => view.value.pipelineId, (next) => {
  pipeline.value = next;
});

function change(next: Partial<RunsView>): void {
  void router.replace({ query: runsQuery(route.query, next) });
}

function applyPipeline(): void {
  change({ pipelineId: pipeline.value.trim(), page: 1 });
}

function openRun(run: ChannelRun): void {
  void router.push({ query: runsQuery(route.query, { runId: Number(run.id) }) });
}
</script>

<template>
  <PanelCard
    data-runs-tab
    :title="t('dashboard.admin.console.runs.title')"
    :description="t('dashboard.admin.console.runs.description')"
    :state="state"
    :empty-text="t('dashboard.admin.console.runs.empty')"
    :error-text="t('dashboard.admin.console.runs.error')"
    @retry="diagnostics.refetch()"
  >
    <template #actions>
      <Input
        v-model="pipeline"
        data-run-pipeline
        class="w-48"
        :aria-label="t('dashboard.admin.console.runs.filters.pipeline')"
        :placeholder="t('dashboard.admin.console.runs.filters.pipelinePlaceholder')"
        @change="applyPipeline"
      />
      <FilterBar v-model="show" :label="t('dashboard.admin.console.runs.filters.show')" :options="showOptions" />
    </template>
    <ChannelRunsTable :rows="rows" @open="openRun" />
    <template #footer>
      <AdminPager
        :page="view.page"
        :pages="pages"
        @previous="change({ page: view.page - 1 })"
        @next="change({ page: view.page + 1 })"
      />
    </template>
  </PanelCard>
  <RunDetailSheet :channel-id="channelId" :run-id="view.runId" @close="change({ runId: null })" />
</template>
