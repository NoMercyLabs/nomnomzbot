<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The platform admin's user registry: a searchable, role-filtered, paged table. The bot pages and sorts
// (newest first); a filter or a search always goes back to page 1.
import { refDebounced, useLocalStorage } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useAdminUsers, type AdminUser, type AdminUsersParams } from '@/api/adminUsers';
import { ApiError } from '@/api/errors';
import FilterBar from '@/components/layout/FilterBar.vue';
import type { FilterOption } from '@/components/layout/filterOption';
import PageActionFooter from '@/components/layout/PageActionFooter.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import PanelBeside from '@/components/layout/PanelBeside.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import { useScreenFrom } from '@/composables/useLayout';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './components/AdminPager.vue';
import UserDetailPanel from './components/UserDetailPanel.vue';
import UsersTable from './components/UsersTable.vue';

const PAGE_SIZE: number = 25;
const SEARCH_PAUSE_MS: number = 300;

const { t } = useI18n();
const tablet = useScreenFrom('tablet');
const route = useRoute();
const router = useRouter();

const search = ref('');
const typed = refDebounced(search, SEARCH_PAUSE_MS);
const role = useLocalStorage<string>('nnz.admin-users.role', 'all');
const page = ref(1);

watch([typed, role], () => {
  page.value = 1;
});

const params = computed(() => ({
  search: typed.value.trim(),
  role: role.value === 'all'
    ? undefined
    : role.value,
  page: page.value,
  take: PAGE_SIZE,
}) as const satisfies AdminUsersParams);

const users = useAdminUsers(params);

const rows = computed<AdminUser[]>(() => users.data.value?.data ?? []);
const total = computed(() => Number(users.data.value?.total ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

const roleOptions = computed<FilterOption[]>(() =>
  ['all', 'admin', 'user'].map((value) => ({
    value,
    label: t(`dashboard.admin.users.filters.role.${value}`),
  }) as const satisfies FilterOption),
);

const panelState = usePanelState(users, (data) =>
  (data.data?.length ?? 0) === 0);
const listError = computed<string>(() =>
  users.error.value instanceof ApiError && users.error.value.status === 403
    ? t('dashboard.admin.users.list.forbidden')
    : t('dashboard.admin.users.list.error'),
);

const selectedId = computed<string>(() =>
  typeof route.query.user === 'string'
    ? route.query.user
    : '',
);
const selected = computed<AdminUser | null>(() =>
  rows.value.find((row) => row.id === selectedId.value) ?? null);

// The drawer is open while a user of this page is in the address; closing it drops the user from there.
const detailOpen = computed<boolean>({
  get: () => selected.value !== null,
  set: (open) => {
    if (!open) {
      void router.push({ query: { ...route.query, user: undefined } });
    }
  },
});

function pick(row: AdminUser): void {
  void router.push({ query: { ...route.query, user: row.id } });
}
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.users.title')" :crumb="$t('dashboard.admin.crumb')">
    <template #status>
      <span v-if="users.data.value" class="text-sm text-muted-foreground tabular-nums">
        {{ $t('dashboard.admin.users.count', { count: total }) }}
      </span>
    </template>
    <template #bar>
      <div class="flex min-w-0 flex-1 items-center gap-x-4 gap-y-2 max-md:flex-wrap md:overflow-x-auto md:scrollbar-none md:justify-end">
        <div class="flex shrink-0 items-center gap-2">
          <span data-filter-label class="text-sm text-muted-foreground">
            {{ $t('dashboard.admin.users.filters.roleLabel') }}
          </span>
          <FilterBar
            v-model="role"
            :label="$t('dashboard.admin.users.filters.roleLabel')"
            :options="roleOptions"
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
        :title="$t('dashboard.admin.users.list.title')"
        :description="$t('dashboard.admin.users.list.newestFirst')"
        :state="panelState"
        :empty-text="$t('dashboard.admin.users.list.empty')"
        :error-text="listError"
        @retry="users.refetch()"
      >
        <template #actions>
          <!-- Concentric with the panel corner: 8px from the top and the right, so a 20 - 8 = 12px radius. -->
          <SearchField
            v-model="search"
            class="-mr-2 w-52 rounded-[12px]"
            :placeholder="$t('dashboard.admin.users.search')"
          />
        </template>
        <UsersTable :rows="rows" :selected-id="selectedId" @pick="pick" />
        <template v-if="tablet" #footer>
          <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
        </template>
      </PanelCard>
      <!-- On a phone the list takes its full height and the pager rides in the sticky page footer. -->
      <PageActionFooter v-if="!tablet">
        <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
      </PageActionFooter>
    </div>
    <template #panel>
      <SidePanel v-model:open="detailOpen" :label="$t('dashboard.admin.users.detail.title')">
        <UserDetailPanel v-if="selected" :user="selected" />
      </SidePanel>
    </template>
  </PanelBeside>
</template>
