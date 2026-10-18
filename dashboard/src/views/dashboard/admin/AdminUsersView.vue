<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform admin's user registry: a searchable, role-filtered, paged table. The bot pages and sorts
// (newest first); a filter or a search always goes back to page 1.
import { refDebounced, useLocalStorage } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import ChevronLeft from '~icons/nnz/chevron-left';
import ChevronRight from '~icons/nnz/chevron-right';

import { useAdminUsers, type AdminUser, type AdminUsersParams } from '@/api/adminUsers';
import { ApiError } from '@/api/errors';
import FilterBar from '@/components/layout/FilterBar.vue';
import type { FilterOption } from '@/components/layout/filterOption';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';

import UsersTable from './components/UsersTable.vue';

const PAGE_SIZE: number = 25;
const SEARCH_PAUSE_MS: number = 300;

const { t } = useI18n();

const search = ref('');
const typed = refDebounced(search, SEARCH_PAUSE_MS);
const role = useLocalStorage<string>('nnz.admin-users.role', 'all');
const page = ref(1);

watch([typed, role], () => {
  page.value = 1;
});

const params = computed<AdminUsersParams>(() => ({
  search: typed.value.trim(),
  role: role.value === 'all'
    ? undefined
    : role.value,
  page: page.value,
  take: PAGE_SIZE,
}));

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
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.users.title')" :crumb="$t('dashboard.admin.crumb')">
    <template #status>
      <span v-if="users.data.value" class="text-sm text-muted-foreground tabular-nums">
        {{ $t('dashboard.admin.users.count', { count: total }) }}
      </span>
    </template>
    <template #bar>
      <div class="flex min-w-0 flex-1 items-center gap-4 overflow-x-auto scrollbar-none md:justify-end">
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
      <UsersTable :rows="rows" />
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
