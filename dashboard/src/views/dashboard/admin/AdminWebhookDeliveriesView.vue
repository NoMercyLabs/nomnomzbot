<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
import { usePanelState } from '@/composables/usePanelState';

import AdminCollectionPage from './components/AdminCollectionPage.vue';
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

const count = computed<string | undefined>(() =>
  log.data.value
    ? t('dashboard.admin.webhookDeliveries.count', { count: total.value })
    : undefined);

function replayOne(id: number): void {
  replay.mutate(id, {
    onSuccess: () => toast.success(t('dashboard.admin.webhookDeliveries.replayed')),
    onError: (error: Error) =>
      toast.error(t('dashboard.admin.webhookDeliveries.replayError', { reason: error.message })),
  });
}
</script>

<template>
  <AdminCollectionPage
    :title="$t('dashboard.admin.webhookDeliveries.title')"
    :count="count"
    :page="page"
    :pages="pages"
    :state="panelState"
    :list-title="$t('dashboard.admin.webhookDeliveries.list.title')"
    :description="$t('dashboard.admin.webhookDeliveries.list.newestFirst')"
    :empty-text="$t('dashboard.admin.webhookDeliveries.list.empty')"
    :error-text="listError"
    @previous="page -= 1"
    @next="page += 1"
    @retry="log.refetch()"
  >
    <WebhookDeliveriesTable
      :rows="rows"
      :replaying="replay.isPending.value"
      @replay="replayOne"
    />
  </AdminCollectionPage>
</template>
