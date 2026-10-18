<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './components/AdminPager.vue';
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

function retryOne(id: string): void {
  retry.mutate(id, {
    onSuccess: () => toast.success(t('dashboard.admin.scheduledJobs.retried')),
    onError: (error: Error) =>
      toast.error(t('dashboard.admin.scheduledJobs.retryError', { reason: error.message })),
  });
}
</script>

<template>
  <PageHeader
    :title="$t('dashboard.admin.scheduledJobs.title')"
    :crumb="$t('dashboard.admin.crumb')"
  >
    <template #status>
      <span v-if="queue.data.value" class="text-sm text-muted-foreground tabular-nums">
        {{ $t("dashboard.admin.scheduledJobs.count", { count: total }) }}
      </span>
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <PanelCard
      tier="lead"
      class="min-h-80 min-w-0 flex-1"
      :title="$t('dashboard.admin.scheduledJobs.list.title')"
      :description="$t('dashboard.admin.scheduledJobs.list.newestFirst')"
      :state="panelState"
      :empty-text="$t('dashboard.admin.scheduledJobs.list.empty')"
      :error-text="listError"
      @retry="queue.refetch()"
    >
      <ScheduledJobsTable :rows="rows" :retrying="retry.isPending.value" @retry="retryOne" />
      <template #footer>
        <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
      </template>
    </PanelCard>
  </div>
</template>
