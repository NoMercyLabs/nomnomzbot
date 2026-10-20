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
// counts by status on the status filter. An operator retries a failed erasure after the counted blast radius is shown.
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { useTime } from '@/composables/useTime';

// cspell:ignore cancelled (the API wire value for a canceled request)
const statuses = ['pending', 'running', 'completed', 'failed', 'cancelled'] as const;
// The labels use American English.
const statusLabelKey = (key: string) =>
  `dashboard.admin.dataRequests.status.${key === 'cancelled'
    ? 'canceled'
    : key}`;
const types = ['erasure', 'export', 'opt_out'] as const;

const { t, te } = useI18n();
const time = useTime();
const { can } = usePlatformCan();

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

const statusOptions = computed(() => {
  const data = summary.data.value;
  const counted = (label: string, count: string | number | undefined): string =>
    count === undefined
      ? label
      : t('dashboard.admin.dataRequests.filters.statusOption', { label, count });

  return [
    {
      key: 'all',
      label: counted(t('dashboard.admin.dataRequests.status.all'), data?.total),
    },
    ...statuses.map((key) => {
      const label = t(statusLabelKey(key));

      return {
        key,
        label: counted(label, data?.[key]),
      };
    }),
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

function requesterOf(row: ErasureRequest): string {
  const key = `dashboard.admin.dataRequests.requestedBy.${row.requestedBy}`;

  return t('dashboard.admin.dataRequests.list.by', {
    by: te(key)
      ? t(key)
      : row.requestedBy,
  });
}

function rowsOf(row: ErasureRequest): string {
  const key = `dashboard.admin.dataRequests.list.rows.${row.requestType}`;

  return t(
    te(key)
      ? key
      : 'dashboard.admin.dataRequests.list.rows.opt_out',
    { count: row.rowsAffected },
  );
}

function when(value: string): string {
  return time.dateTime(value);
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
    <div class="flex flex-wrap items-center gap-2">
      <Select v-model="status">
        <SelectTrigger
          data-status-filter
          :aria-label="$t('dashboard.admin.dataRequests.filters.status')"
          class="w-48"
        >
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="option in statusOptions" :key="option.key" :value="option.key">
            {{ option.label }}
          </SelectItem>
        </SelectContent>
      </Select>
      <Select v-model="requestType">
        <SelectTrigger
          data-type-filter
          :aria-label="$t('dashboard.admin.dataRequests.filters.type')"
          class="w-40"
        >
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">{{ $t("dashboard.admin.dataRequests.type.all") }}</SelectItem>
          <SelectItem v-for="key in types" :key="key" :value="key">
            {{ $t(`dashboard.admin.dataRequests.type.${key}`) }}
          </SelectItem>
        </SelectContent>
      </Select>
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
              <span>{{ requesterOf(row) }}</span>
              <span>{{ rowsOf(row) }}</span>
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
            v-if="row.status === 'failed' && row.requestType === 'erasure' && can('compliance:erasure')"
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
