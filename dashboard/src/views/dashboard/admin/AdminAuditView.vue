<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The platform audit log: every evaluated operator permission, newest first, filtered and paged.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useAdminAudit, type AdminAuditEntry, type AdminAuditParams } from '@/api/adminAudit';
import { ApiError } from '@/api/errors';
import { usePanelState } from '@/composables/usePanelState';

import AdminCollectionPage from './components/AdminCollectionPage.vue';
import AuditFilters, { type AuditFilterValues } from './components/AuditFilters.vue';
import AuditTable from './components/AuditTable.vue';

const PAGE_SIZE: number = 25;

const { t } = useI18n();
const page = ref(1);
const filters = ref<AuditFilterValues>({
  permission: '',
  outcome: '',
  principalId: '',
  targetBroadcasterId: '',
  from: '',
  to: '',
});

// A new filter starts again at the first page.
watch(filters, () => {
  page.value = 1;
});

const params = computed(() => ({
  ...filters.value,
  page: page.value,
  take: PAGE_SIZE,
}) as const satisfies AdminAuditParams);

const log = useAdminAudit(params);

const rows = computed<AdminAuditEntry[]>(() => log.data.value?.data ?? []);
const total = computed(() => Number(log.data.value?.total ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

const panelState = usePanelState(log, (data) =>
  (data.data?.length ?? 0) === 0);
const listError = computed<string>(() =>
  log.error.value instanceof ApiError && log.error.value.status === 403
    ? t('dashboard.admin.audit.list.forbidden')
    : t('dashboard.admin.audit.list.error'),
);
const count = computed<string | undefined>(() =>
  log.data.value
    ? t('dashboard.admin.audit.count', { count: total.value })
    : undefined);
</script>

<template>
  <AdminCollectionPage
    :title="$t('dashboard.admin.audit.title')"
    :count="count"
    :page="page"
    :pages="pages"
    :state="panelState"
    :list-title="$t('dashboard.admin.audit.list.title')"
    :description="$t('dashboard.admin.audit.list.newestFirst')"
    :empty-text="$t('dashboard.admin.audit.list.empty')"
    :error-text="listError"
    @previous="page -= 1"
    @next="page += 1"
    @retry="log.refetch()"
  >
    <template #filters>
      <AuditFilters v-model="filters" />
    </template>
    <AuditTable :rows="rows" />
  </AdminCollectionPage>
</template>
