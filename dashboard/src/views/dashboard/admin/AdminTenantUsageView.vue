<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Per-tenant metered usage for each tenant's latest period, straight from recorded usage rows.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminPageParams } from '@/api/adminActivity';
import { useAdminTenantUsage, type AdminTenantUsage } from '@/api/adminUsage';
import { ApiError } from '@/api/errors';
import { usePanelState } from '@/composables/usePanelState';

import AdminCollectionPage from './components/AdminCollectionPage.vue';
import TenantUsageTable from './components/TenantUsageTable.vue';

const PAGE_SIZE: number = 25;

const { t } = useI18n();

const page = ref(1);

const params = computed(() =>
  ({ page: page.value, take: PAGE_SIZE }) as const satisfies AdminPageParams);

const usage = useAdminTenantUsage(params);

const rows = computed<AdminTenantUsage[]>(() => usage.data.value?.data ?? []);
const total = computed(() => Number(usage.data.value?.total ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

const panelState = usePanelState(usage, (data) =>
  (data.data?.length ?? 0) === 0);
const listError = computed<string>(() =>
  usage.error.value instanceof ApiError && usage.error.value.status === 403
    ? t('dashboard.admin.tenantUsage.list.forbidden')
    : t('dashboard.admin.tenantUsage.list.error'),
);
const count = computed<string | undefined>(() =>
  usage.data.value
    ? t('dashboard.admin.tenantUsage.count', { count: total.value })
    : undefined);
</script>

<template>
  <AdminCollectionPage
    :title="$t('dashboard.admin.tenantUsage.title')"
    :count="count"
    :page="page"
    :pages="pages"
    :state="panelState"
    :list-title="$t('dashboard.admin.tenantUsage.list.title')"
    :description="$t('dashboard.admin.tenantUsage.list.latestPeriod')"
    :empty-text="$t('dashboard.admin.tenantUsage.list.empty')"
    :error-text="listError"
    @previous="page -= 1"
    @next="page += 1"
    @retry="usage.refetch()"
  >
    <template #notice>
      <p data-page-inset class="text-sm text-muted-foreground">{{ $t("dashboard.admin.tenantUsage.restricted") }}</p>
    </template>
    <TenantUsageTable :rows="rows" />
  </AdminCollectionPage>
</template>
