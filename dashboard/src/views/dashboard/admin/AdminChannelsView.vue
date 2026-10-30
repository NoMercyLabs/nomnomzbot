<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The platform admin's channel list (the old Channels and Tenants pages in one): a searchable, filterable,
// paged table. The bot pages and sorts (newest first); a filter or a search always goes back to page 1.
// A row opens the channel's console. ?search= starts the search, so a copied link reopens the same list.
import { refDebounced, useLocalStorage } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useAdminTenants, type AdminTenant, type AdminTenantsParams } from '@/api/admin';
import { ApiError } from '@/api/errors';
import FilterBar from '@/components/layout/FilterBar.vue';
import type { FilterOption } from '@/components/layout/filterOption';
import PageActionFooter from '@/components/layout/PageActionFooter.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import { useScreenFrom } from '@/composables/useLayout';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './components/AdminPager.vue';
import ChannelsTable from './components/ChannelsTable.vue';
import SupportContactPanel from './components/SupportContactPanel.vue';

const PAGE_SIZE: number = 25;
const SEARCH_PAUSE_MS: number = 300;

const { t } = useI18n();
const tablet = useScreenFrom('tablet');
const route = useRoute();
const router = useRouter();

const search = ref(typeof route.query.search === 'string'
  ? route.query.search
  : '');
const typed = refDebounced(search, SEARCH_PAUSE_MS);
const status = useLocalStorage<string>('nnz.admin.status', 'all');
const live = useLocalStorage<string>('nnz.admin.live', 'all');
const page = ref(1);

watch([typed, status, live], () => {
  page.value = 1;
});

const params = computed(() => ({
  search: typed.value.trim(),
  status: status.value === 'all'
    ? undefined
    : status.value,
  isLive: live.value === 'all'
    ? undefined
    : live.value === 'live',
  page: page.value,
  take: PAGE_SIZE,
}) as const satisfies AdminTenantsParams);

const tenants = useAdminTenants(params);

const rows = computed<AdminTenant[]>(() => tenants.data.value?.data ?? []);
const total = computed(() => Number(tenants.data.value?.total ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

const statusOptions = computed<FilterOption[]>(() =>
  ['all', 'active', 'suspended', 'platform_banned'].map((value) => ({
    value,
    label: t(`dashboard.admin.filters.status.${value}`),
  }) as const satisfies FilterOption),
);
const liveOptions = computed<FilterOption[]>(() =>
  ['all', 'live', 'offline'].map((value) => ({
    value,
    label: t(`dashboard.admin.filters.live.${value}`),
  }) as const satisfies FilterOption),
);

const panelState = usePanelState(tenants, (data) =>
  (data.data?.length ?? 0) === 0);
const listError = computed<string>(() =>
  tenants.error.value instanceof ApiError && tenants.error.value.status === 403
    ? t('dashboard.admin.list.forbidden')
    : t('dashboard.admin.list.error'),
);

function openConsole(row: AdminTenant): void {
  void router.push({ name: 'dashboard-admin-channels-console', params: { channelId: row.id } });
}
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.title')" :crumb="$t('dashboard.admin.crumb')">
    <template #status>
      <span v-if="tenants.data.value" class="text-sm text-muted-foreground tabular-nums">
        {{ $t('dashboard.admin.count', { count: total }) }}
      </span>
    </template>
    <template #bar>
      <div class="flex min-w-0 flex-1 items-center gap-x-4 gap-y-2 max-md:flex-wrap md:overflow-x-auto md:scrollbar-none md:justify-end">
        <div class="flex shrink-0 items-center gap-2">
          <span data-filter-label class="text-sm text-muted-foreground">
            {{ $t('dashboard.admin.filters.statusLabel') }}
          </span>
          <FilterBar
            v-model="status"
            :label="$t('dashboard.admin.filters.statusLabel')"
            :options="statusOptions"
          />
        </div>
        <div class="flex shrink-0 items-center gap-2">
          <span data-filter-label class="text-sm text-muted-foreground">
            {{ $t('dashboard.admin.filters.liveLabel') }}
          </span>
          <FilterBar
            v-model="live"
            :label="$t('dashboard.admin.filters.liveLabel')"
            :options="liveOptions"
          />
        </div>
      </div>
    </template>
  </PageHeader>
  <PageBody data-page-body>
    <PanelCard
      tier="lead"
      class="min-h-80 min-w-0 flex-1"
      :title="$t('dashboard.admin.list.title')"
      :description="$t('dashboard.admin.list.newestFirst')"
      :state="panelState"
      :empty-text="$t('dashboard.admin.list.empty')"
      :error-text="listError"
      @retry="tenants.refetch()"
    >
      <template #actions>
        <!-- Concentric with the panel corner: 8px from the top and the right, so a 20 - 8 = 12px radius. -->
        <SearchField
          v-model="search"
          class="-mr-2 w-52 rounded-[12px]"
          :placeholder="$t('dashboard.admin.search')"
        />
      </template>
      <ChannelsTable :rows="rows" @open="openConsole" />
      <template v-if="tablet" #footer>
        <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
      </template>
    </PanelCard>
    <!-- On a phone the list takes its full height and the pager rides in the sticky page footer. -->
    <PageActionFooter v-if="!tablet">
      <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
    </PageActionFooter>
    <SupportContactPanel />
  </PageBody>
</template>
