<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform admin's channel registry: a searchable, filterable, paged table. The bot pages and sorts
// (newest first); a filter or a search always goes back to page 1. The channel in view is ?channel=.
import { refDebounced, useLocalStorage } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';
import ChevronLeft from '~icons/nnz/chevron-left';
import ChevronRight from '~icons/nnz/chevron-right';

import { useAdminTenants, type AdminTenant, type AdminTenantsParams } from '@/api/admin';
import FilterBar, { type FilterOption } from '@/components/layout/FilterBar.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';

import ChannelsTable from './components/ChannelsTable.vue';

const PAGE_SIZE: number = 25;
const SEARCH_PAUSE_MS: number = 300;

const { t } = useI18n();
const route = useRoute();
const router = useRouter();

const search = ref('');
const typed = refDebounced(search, SEARCH_PAUSE_MS);
const status = useLocalStorage<string>('nnz.admin.status', 'all');
const live = useLocalStorage<string>('nnz.admin.live', 'all');
const page = ref(1);

watch([typed, status, live], () => {
  page.value = 1;
});

const params = computed<AdminTenantsParams>(() => ({
  search: typed.value.trim(),
  status: status.value === 'all'
    ? undefined
    : status.value,
  isLive: live.value === 'all'
    ? undefined
    : live.value === 'live',
  page: page.value,
  take: PAGE_SIZE,
}));

const tenants = useAdminTenants(params);

const rows = computed<AdminTenant[]>(() => tenants.data.value?.data ?? []);
const total = computed(() => Number(tenants.data.value?.total ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

const statusOptions = computed<FilterOption[]>(() =>
  ['all', 'active', 'suspended', 'platform_banned'].map((value: string): FilterOption => ({
    value,
    label: t(`dashboard.admin.filters.status.${value}`),
  })),
);
const liveOptions = computed<FilterOption[]>(() =>
  ['all', 'live', 'offline'].map((value: string): FilterOption => ({
    value,
    label: t(`dashboard.admin.filters.live.${value}`),
  })),
);

const panelState = usePanelState(tenants, (data) =>
  (data.data?.length ?? 0) === 0);
const errorText = computed<string>(() =>
  tenants.error.value?.message.endsWith('403')
    ? t('dashboard.admin.list.forbidden')
    : t('dashboard.admin.list.error'),
);

const selectedId = computed<string>(() =>
  typeof route.query.channel === 'string'
    ? route.query.channel
    : '',
);

function pick(row: AdminTenant): void {
  void router.push({ query: { ...route.query, channel: row.id } });
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
      <div class="flex flex-wrap items-center gap-4">
        <FilterBar
          v-model="status"
          :label="$t('dashboard.admin.filters.statusLabel')"
          :options="statusOptions"
        >
          <SearchField v-model="search" :placeholder="$t('dashboard.admin.search')" />
        </FilterBar>
        <FilterBar
          v-model="live"
          :label="$t('dashboard.admin.filters.liveLabel')"
          :options="liveOptions"
        />
      </div>
    </template>
  </PageHeader>
  <div class="flex min-h-0 flex-1 flex-col p-6">
    <PanelCard
      tier="lead"
      class="min-h-80 flex-1"
      :title="$t('dashboard.admin.list.title')"
      :description="$t('dashboard.admin.list.newestFirst')"
      :state="panelState"
      :empty-text="$t('dashboard.admin.list.empty')"
      :error-text="errorText"
      @retry="tenants.refetch()"
    >
      <ChannelsTable :rows="rows" :selected-id="selectedId" @pick="pick" />
      <template #footer>
        <span class="text-sm text-muted-foreground tabular-nums" role="status">
          {{ $t('dashboard.admin.pager.page', { page, pages }) }}
        </span>
        <div class="ml-auto flex items-center gap-2">
          <Button variant="outline" size="sm" :disabled="page <= 1" @click="page -= 1">
            <ChevronLeft aria-hidden="true" />
            {{ $t('dashboard.admin.pager.previous') }}
          </Button>
          <Button variant="outline" size="sm" :disabled="page >= pages" @click="page += 1">
            {{ $t('dashboard.admin.pager.next') }}
            <ChevronRight aria-hidden="true" />
          </Button>
        </div>
      </template>
    </PanelCard>
  </div>
</template>
