<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Per-tenant metered usage for each tenant's latest period, straight from recorded usage rows.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminPageParams } from '@/api/adminActivity';
import { useAdminTenantUsage, type AdminTenantUsage } from '@/api/adminUsage';
import { ApiError } from '@/api/errors';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './components/AdminPager.vue';
import TenantUsageTable from './components/TenantUsageTable.vue';

const PAGE_SIZE: number = 25;

const { t } = useI18n();

const page = ref(1);

const params = computed<AdminPageParams>(() => ({ page: page.value, take: PAGE_SIZE }));

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
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.tenantUsage.title')" :crumb="$t('dashboard.admin.crumb')">
    <template #status>
      <span v-if="usage.data.value" class="text-sm text-muted-foreground tabular-nums">
        {{ $t("dashboard.admin.tenantUsage.count", { count: total }) }}
      </span>
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <p class="text-sm text-muted-foreground">{{ $t("dashboard.admin.tenantUsage.restricted") }}</p>
    <PanelCard
      tier="lead"
      class="min-h-80 min-w-0 flex-1"
      :title="$t('dashboard.admin.tenantUsage.list.title')"
      :description="$t('dashboard.admin.tenantUsage.list.latestPeriod')"
      :state="panelState"
      :empty-text="$t('dashboard.admin.tenantUsage.list.empty')"
      :error-text="listError"
      @retry="usage.refetch()"
    >
      <TenantUsageTable :rows="rows" />
      <template #footer>
        <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
      </template>
    </PanelCard>
  </div>
</template>
