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
import PageActionFooter from '@/components/layout/PageActionFooter.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { useScreenFrom } from '@/composables/useLayout';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './components/AdminPager.vue';
import AuditFilters, { type AuditFilterValues } from './components/AuditFilters.vue';
import AuditTable from './components/AuditTable.vue';

const PAGE_SIZE: number = 25;

const { t } = useI18n();
const tablet = useScreenFrom('tablet');

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
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.audit.title')" :crumb="$t('dashboard.admin.crumb')">
    <template #status>
      <span v-if="log.data.value" class="text-sm text-muted-foreground tabular-nums">
        {{ $t("dashboard.admin.audit.count", { count: total }) }}
      </span>
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <AuditFilters v-model="filters" />
    <PanelCard
      tier="lead"
      class="min-h-80 min-w-0 flex-1"
      :title="$t('dashboard.admin.audit.list.title')"
      :description="$t('dashboard.admin.audit.list.newestFirst')"
      :state="panelState"
      :empty-text="$t('dashboard.admin.audit.list.empty')"
      :error-text="listError"
      @retry="log.refetch()"
    >
      <AuditTable :rows="rows" />
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
