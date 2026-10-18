<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The cross-channel outbound webhook log: every attempt, newest first, paged. A replay adds a new attempt row.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  useAdminWebhookDeliveries,
  useAdminWebhookReplay,
  type AdminPageParams,
  type AdminWebhookDelivery,
} from '@/api/adminActivity';
import { ApiError } from '@/api/errors';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './components/AdminPager.vue';
import WebhookDeliveriesTable from './components/WebhookDeliveriesTable.vue';

const PAGE_SIZE: number = 25;

const { t } = useI18n();

const page = ref(1);

const params = computed(() =>
  ({ page: page.value, take: PAGE_SIZE }) as const satisfies AdminPageParams);

const log = useAdminWebhookDeliveries(params);
const replay = useAdminWebhookReplay();

const rows = computed<AdminWebhookDelivery[]>(() => log.data.value?.data ?? []);
const total = computed(() => Number(log.data.value?.total ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

const panelState = usePanelState(log, (data) =>
  (data.data?.length ?? 0) === 0);
const listError = computed<string>(() =>
  log.error.value instanceof ApiError && log.error.value.status === 403
    ? t('dashboard.admin.webhookDeliveries.list.forbidden')
    : t('dashboard.admin.webhookDeliveries.list.error'),
);

function replayOne(id: number): void {
  replay.mutate(id, {
    onSuccess: () => toast.success(t('dashboard.admin.webhookDeliveries.replayed')),
    onError: (error: Error) =>
      toast.error(t('dashboard.admin.webhookDeliveries.replayError', { reason: error.message })),
  });
}
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.webhookDeliveries.title')" :crumb="$t('dashboard.admin.crumb')">
    <template #status>
      <span v-if="log.data.value" class="text-sm text-muted-foreground tabular-nums">
        {{ $t('dashboard.admin.webhookDeliveries.count', { count: total }) }}
      </span>
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <PanelCard
      tier="lead"
      class="min-h-80 min-w-0 flex-1"
      :title="$t('dashboard.admin.webhookDeliveries.list.title')"
      :description="$t('dashboard.admin.webhookDeliveries.list.newestFirst')"
      :state="panelState"
      :empty-text="$t('dashboard.admin.webhookDeliveries.list.empty')"
      :error-text="listError"
      @retry="log.refetch()"
    >
      <WebhookDeliveriesTable
        :rows="rows"
        :replaying="replay.isPending.value"
        @replay="replayOne"
      />
      <template #footer>
        <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
      </template>
    </PanelCard>
  </div>
</template>
