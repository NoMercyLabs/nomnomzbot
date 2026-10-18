<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot, free software licensed under the GNU Affero
   General Public License v3.0 or later. You may redistribute and/or modify it
   under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

   SPDX-License-Identifier: AGPL-3.0-or-later
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// Data requests: the platform ledger of export, erasure and opt-out requests, newest first, with the
// counts by status. An operator retries a failed erasure after the counted blast radius is shown.
// A subject shows as a hash, never by name. A change arrives over the admin hub.
import { useLocalStorage } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  allRequests,
  useErasurePreview,
  useErasureRequests,
  useErasureSummary,
  useRetryErasure,
  type ErasureRequest,
} from '@/api/adminDataRequests';
import FormDialog from '@/components/layout/FormDialog.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';

// cspell:ignore cancelled (the API wire value for a canceled request)
const statuses = ['pending', 'running', 'completed', 'failed', 'cancelled'] as const;
// The labels use American English.
const statusLabelKey = (key: string) =>
  `dashboard.admin.dataRequests.status.${key === 'cancelled'
    ? 'canceled'
    : key}`;
const types = ['erasure', 'export', 'opt_out'] as const;

const { t } = useI18n();

const status = useLocalStorage<string>('nnz.adminDataRequests.status', allRequests);
const requestType = useLocalStorage<string>('nnz.adminDataRequests.type', allRequests);
const retrying = ref<ErasureRequest | null>(null);
const failure = ref<string | null>(null);

const filter = computed(() => ({
  status: status.value,
  requestType: requestType.value,
}));

const requests = useErasureRequests(filter);
const summary = useErasureSummary();
const preview = useErasurePreview(retrying);
const retry = useRetryErasure();

const listState = usePanelState(requests, (rows) =>
  rows.length === 0);
const summaryState = usePanelState(summary);

const counts = computed(() => {
  const data = summary.data.value;

  return [
    {
      key: 'total',
      count: data?.total ?? 0,
    },
    ...statuses.map((key) => ({
      key,
      count: data?.[key] ?? 0,
    })),
  ];
});

const consequence = computed<string | undefined>(() => {
  const counted = preview.data.value;

  if (preview.isError.value)
    return t('dashboard.admin.dataRequests.retry.error');

  if (counted === null || counted === undefined)
    return t('dashboard.admin.dataRequests.retry.loading');

  if (counted.categories.length === 0)
    return t('dashboard.admin.dataRequests.retry.nothing');

  return t('dashboard.admin.dataRequests.retry.consequence', {
    rows: counted.totalRows,
    categories: counted.categories.length,
  });
});

const error = computed<string | undefined>(() => failure.value ?? undefined);

function hashOf(row: ErasureRequest): string {
  return row.subjectIdHash.slice(0, 12);
}

function when(value: string): string {
  return new Date(value).toLocaleString();
}

function openRetry(row: ErasureRequest): void {
  failure.value = null;
  retrying.value = row;
}

function close(): void {
  retrying.value = null;
}

async function submit(): Promise<void> {
  const row = retrying.value;

  if (row === null || preview.data.value === undefined || preview.data.value === null) return;

  try {
    await retry.mutateAsync(row.id);
    retrying.value = null;
  } catch {
    failure.value = t('dashboard.admin.dataRequests.failed');
  }
}
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.dataRequests.title')" :crumb="$t('dashboard.admin.crumb')" />
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <PanelCard
      tier="quiet"
      :title="$t('dashboard.admin.dataRequests.summary.title')"
      :description="$t('dashboard.admin.dataRequests.summary.description')"
      :state="summaryState"
      :error-text="$t('dashboard.admin.dataRequests.list.error')"
      @retry="summary.refetch()"
    >
      <dl data-summary class="flex flex-wrap gap-x-6 gap-y-2">
        <div v-for="item in counts" :key="item.key" class="grid gap-0.5">
          <dt class="text-xs text-muted-foreground">
            {{
              item.key === "total"
                ? $t("dashboard.admin.dataRequests.summary.total")
                : $t(statusLabelKey(item.key))
            }}
          </dt>
          <dd class="text-lg font-semibold tabular-nums">{{ item.count }}</dd>
        </div>
      </dl>
    </PanelCard>

    <div class="flex flex-wrap items-center gap-2">
      <select
        v-model="status"
        data-status-filter
        :aria-label="$t('dashboard.admin.dataRequests.filters.status')"
        class="h-9 rounded-md border border-input bg-transparent px-3 text-sm"
      >
        <option value="all">{{ $t("dashboard.admin.dataRequests.status.all") }}</option>
        <option v-for="key in statuses" :key="key" :value="key">
          {{ $t(statusLabelKey(key)) }}
        </option>
      </select>
      <select
        v-model="requestType"
        data-type-filter
        :aria-label="$t('dashboard.admin.dataRequests.filters.type')"
        class="h-9 rounded-md border border-input bg-transparent px-3 text-sm"
      >
        <option value="all">{{ $t("dashboard.admin.dataRequests.type.all") }}</option>
        <option v-for="key in types" :key="key" :value="key">
          {{ $t(`dashboard.admin.dataRequests.type.${key}`) }}
        </option>
      </select>
    </div>

    <PanelCard
      tier="lead"
      :title="$t('dashboard.admin.dataRequests.list.title')"
      :description="$t('dashboard.admin.dataRequests.list.description')"
      :state="listState"
      :empty-text="$t('dashboard.admin.dataRequests.list.empty')"
      :error-text="$t('dashboard.admin.dataRequests.list.error')"
      @retry="requests.refetch()"
    >
      <ul class="divide-y divide-border">
        <li
          v-for="row in requests.data.value ?? []"
          :key="row.id"
          data-request-row
          class="flex flex-wrap items-center gap-3 py-3"
        >
          <div class="min-w-0 flex-1">
            <span class="flex flex-wrap gap-x-3 text-sm font-medium">
              <span>{{ $t(`dashboard.admin.dataRequests.type.${row.requestType}`) }}</span>
              <span>{{
                $t("dashboard.admin.dataRequests.list.subject", { hash: hashOf(row) })
              }}</span>
            </span>
            <span class="flex flex-wrap gap-x-3 text-xs text-muted-foreground">
              <span>{{ $t("dashboard.admin.dataRequests.list.by", { by: row.requestedBy }) }}</span>
              <span>{{
                $t("dashboard.admin.dataRequests.list.rows", { count: row.rowsAffected })
              }}</span>
              <span>{{
                $t("dashboard.admin.dataRequests.list.requested", { when: when(row.requestedAt) })
              }}</span>
            </span>
            <span v-if="row.failureReason" class="block text-xs text-destructive">{{
              row.failureReason
            }}</span>
          </div>
          <Badge :variant="row.status === 'failed' ? 'destructive' : 'outline'">{{
            $t(statusLabelKey(row.status))
          }}</Badge>
          <Button
            v-if="row.status === 'failed' && row.requestType === 'erasure'"
            variant="outline"
            data-retry
            @click="openRetry(row)"
          >{{ $t("dashboard.admin.dataRequests.retry.action") }}</Button>
        </li>
      </ul>
    </PanelCard>

    <FormDialog
      :open="retrying !== null"
      :title="$t('dashboard.admin.dataRequests.retry.title', { hash: retrying ? hashOf(retrying) : '' })"
      :description="consequence"
      :action="$t('dashboard.admin.dataRequests.retry.confirm')"
      :pending="retry.isPending.value"
      :error="error"
      @update:open="close"
      @submit="submit"
    />
  </div>
</template>
