<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The cross-channel background job queue: every scheduled run, newest first, paged. A retry queues a new run.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  useAdminScheduledJobRetry,
  useAdminScheduledJobs,
  type AdminPageParams,
  type AdminScheduledJob,
} from '@/api/adminActivity';
import { ApiError } from '@/api/errors';
import { usePanelState } from '@/composables/usePanelState';

import AdminCollectionPage from './components/AdminCollectionPage.vue';
import ScheduledJobsTable from './components/ScheduledJobsTable.vue';

const PAGE_SIZE: number = 25;

const { t } = useI18n();

const page = ref(1);

const params = computed(() =>
  ({ page: page.value, take: PAGE_SIZE }) as const satisfies AdminPageParams);

const queue = useAdminScheduledJobs(params);
const retry = useAdminScheduledJobRetry();

const rows = computed<AdminScheduledJob[]>(() => queue.data.value?.data ?? []);
const total = computed(() => Number(queue.data.value?.total ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

const panelState = usePanelState(queue, (data) =>
  (data.data?.length ?? 0) === 0);
const listError = computed<string>(() =>
  queue.error.value instanceof ApiError && queue.error.value.status === 403
    ? t('dashboard.admin.scheduledJobs.list.forbidden')
    : t('dashboard.admin.scheduledJobs.list.error'),
);

const count = computed<string | undefined>(() =>
  queue.data.value
    ? t('dashboard.admin.scheduledJobs.count', { count: total.value })
    : undefined);

function retryOne(id: string): void {
  retry.mutate(id, {
    onSuccess: () => toast.success(t('dashboard.admin.scheduledJobs.retried')),
    onError: (error: Error) =>
      toast.error(t('dashboard.admin.scheduledJobs.retryError', { reason: error.message })),
  });
}
</script>

<template>
  <AdminCollectionPage
    :title="$t('dashboard.admin.scheduledJobs.title')"
    :count="count"
    :page="page"
    :pages="pages"
    :state="panelState"
    :list-title="$t('dashboard.admin.scheduledJobs.list.title')"
    :description="$t('dashboard.admin.scheduledJobs.list.newestFirst')"
    :empty-text="$t('dashboard.admin.scheduledJobs.list.empty')"
    :error-text="listError"
    @previous="page -= 1"
    @next="page += 1"
    @retry="queue.refetch()"
  >
    <ScheduledJobsTable :rows="rows" :retrying="retry.isPending.value" @retry="retryOne" />
  </AdminCollectionPage>
</template>
