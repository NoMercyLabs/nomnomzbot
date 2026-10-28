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
import PageActionFooter from '@/components/layout/PageActionFooter.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { useScreenFrom } from '@/composables/useLayout';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './components/AdminPager.vue';
import TenantUsageTable from './components/TenantUsageTable.vue';

const PAGE_SIZE: number = 25;

const { t } = useI18n();

const tablet = useScreenFrom('tablet');
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
      <template v-if="tablet" #footer>
        <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
      </template>
    </PanelCard>
    <!-- On a phone the list takes its full height and the pager rides in the sticky page footer. -->
    <PageActionFooter v-if="!tablet">
      <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
    </PageActionFooter>
  </div>
</template>
