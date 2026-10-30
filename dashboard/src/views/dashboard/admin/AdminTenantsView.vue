<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The platform admin's tenant list: every person who joined, a streamer (full onboarding, owns a channel) or a
// viewer who signed in to link their platforms. A user and their channel are one row, never two lists. The bot
// pages and sorts (newest first); a filter or a search always goes back to page 1. A streamer's row opens the
// channel console; a viewer's row opens the detail beside the list (?user=). ?search= starts the search, so a
// copied link reopens the same list.
import { refDebounced, useLocalStorage } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useAdminTenants, type AdminTenant, type AdminTenantsParams } from '@/api/admin';
import { ApiError } from '@/api/errors';
import FilterBar from '@/components/layout/FilterBar.vue';
import type { FilterOption } from '@/components/layout/filterOption';
import PageActionFooter from '@/components/layout/PageActionFooter.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelBeside from '@/components/layout/PanelBeside.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import { useScreenFrom } from '@/composables/useLayout';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './components/AdminPager.vue';
import SupportContactPanel from './components/SupportContactPanel.vue';
import TenantDetailPanel from './components/TenantDetailPanel.vue';
import TenantsTable from './components/TenantsTable.vue';

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
const kind = useLocalStorage<string>('nnz.admin.kind', 'all');
const status = useLocalStorage<string>('nnz.admin.status', 'all');
const live = useLocalStorage<string>('nnz.admin.live', 'all');
const page = ref(1);

watch([typed, kind, status, live], () => {
  page.value = 1;
});

const params = computed(() => ({
  search: typed.value.trim(),
  kind: kind.value === 'streamer' || kind.value === 'viewer'
    ? kind.value
    : undefined,
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

function optionsOf(group: string, values: readonly string[]): FilterOption[] {
  return values.map((value) => ({
    value,
    label: t(`dashboard.admin.tenants.filters.${group}.${value}`),
  }) as const satisfies FilterOption);
}

const kindOptions = computed<FilterOption[]>(() =>
  optionsOf('kind', ['all', 'streamer', 'viewer']));
const statusOptions = computed<FilterOption[]>(() =>
  optionsOf('status', ['all', 'active', 'suspended', 'platform_banned']));
const liveOptions = computed<FilterOption[]>(() =>
  optionsOf('live', ['all', 'live', 'offline']));

const panelState = usePanelState(tenants, (data) =>
  (data.data?.length ?? 0) === 0);
const listError = computed<string>(() =>
  tenants.error.value instanceof ApiError && tenants.error.value.status === 403
    ? t('dashboard.admin.tenants.list.forbidden')
    : t('dashboard.admin.tenants.list.error'),
);

const selectedId = computed<string>(() =>
  typeof route.query.user === 'string'
    ? route.query.user
    : '',
);
const selected = computed<AdminTenant | null>(() =>
  rows.value.find((row) => row.userId === selectedId.value) ?? null);

// The drawer is open while a tenant of this page is in the address; closing it drops the tenant from there.
const detailOpen = computed<boolean>({
  get: () => selected.value !== null,
  set: (open) => {
    if (!open) {
      void router.push({ query: { ...route.query, user: undefined } });
    }
  },
});

// A streamer owns a channel, so the row opens its console; a viewer has none, so the row opens the detail.
function open({ userId, channel }: AdminTenant): void {
  if (channel) {
    void router.push({
      name: 'dashboard-admin-channels-console',
      params: { channelId: channel.broadcasterId },
    });

    return;
  }

  void router.push({ query: { ...route.query, user: userId } });
}
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.tenants.title')" :crumb="$t('dashboard.admin.crumb')">
    <template #status>
      <span v-if="tenants.data.value" class="text-sm text-muted-foreground tabular-nums">
        {{ $t('dashboard.admin.tenants.count', { count: total }) }}
      </span>
    </template>
    <template #bar>
      <div class="flex min-w-0 flex-1 items-center gap-x-4 gap-y-2 max-md:flex-wrap md:overflow-x-auto md:scrollbar-none md:justify-end">
        <div class="flex shrink-0 items-center gap-2">
          <span data-filter-label class="text-sm text-muted-foreground">
            {{ $t('dashboard.admin.tenants.filters.kindLabel') }}
          </span>
          <FilterBar
            v-model="kind"
            :label="$t('dashboard.admin.tenants.filters.kindLabel')"
            :options="kindOptions"
          />
        </div>
        <div class="flex shrink-0 items-center gap-2">
          <span data-filter-label class="text-sm text-muted-foreground">
            {{ $t('dashboard.admin.tenants.filters.statusLabel') }}
          </span>
          <FilterBar
            v-model="status"
            :label="$t('dashboard.admin.tenants.filters.statusLabel')"
            :options="statusOptions"
          />
        </div>
        <div class="flex shrink-0 items-center gap-2">
          <span data-filter-label class="text-sm text-muted-foreground">
            {{ $t('dashboard.admin.tenants.filters.liveLabel') }}
          </span>
          <FilterBar
            v-model="live"
            :label="$t('dashboard.admin.tenants.filters.liveLabel')"
            :options="liveOptions"
          />
        </div>
      </div>
    </template>
  </PageHeader>
  <PanelBeside>
    <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
      <PanelCard
        tier="lead"
        class="min-h-80 min-w-0 flex-1"
        :title="$t('dashboard.admin.tenants.list.title')"
        :description="$t('dashboard.admin.tenants.list.newestFirst')"
        :state="panelState"
        :empty-text="$t('dashboard.admin.tenants.list.empty')"
        :error-text="listError"
        @retry="tenants.refetch()"
      >
        <template #actions>
          <!-- Concentric with the panel corner: 8px from the top and the right, so a 20 - 8 = 12px radius. -->
          <SearchField
            v-model="search"
            class="-mr-2 w-52 rounded-[12px]"
            :placeholder="$t('dashboard.admin.tenants.search')"
          />
        </template>
        <TenantsTable :rows="rows" :selected-id="selectedId" @open="open" />
        <template v-if="tablet" #footer>
          <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
        </template>
      </PanelCard>
      <!-- On a phone the list takes its full height and the pager rides in the sticky page footer. -->
      <PageActionFooter v-if="!tablet">
        <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
      </PageActionFooter>
      <SupportContactPanel />
    </div>
    <template #panel>
      <SidePanel v-model:open="detailOpen" :label="$t('dashboard.admin.tenants.detail.title')">
        <TenantDetailPanel v-if="selected" :tenant="selected" />
      </SidePanel>
    </template>
  </PanelBeside>
</template>
