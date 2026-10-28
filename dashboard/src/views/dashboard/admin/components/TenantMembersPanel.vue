<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The people who belong to one tenant: owner, management members, community members and seen viewers.
import { refDebounced } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useTenantMembers } from '@/api/adminTenants';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import { usePanelState } from '@/composables/usePanelState';

const props = defineProps<{ tenantId: string }>();

const { t } = useI18n();

const search = ref('');
const typed = refDebounced(search, 300);
const members = useTenantMembers(() => props.tenantId, typed);
const rows = computed(() => members.data.value?.data ?? []);
const state = usePanelState(members, (page) =>
  (page.data?.length ?? 0) === 0);
</script>

<template>
  <PanelCard
    :title="t('dashboard.admin.tenants.members.title')"
    :state="state"
    :empty-text="t('dashboard.admin.tenants.members.empty')"
    :error-text="t('dashboard.admin.tenants.members.error')"
    @retry="members.refetch()"
  >
    <template #actions>
      <SearchField
        v-model="search"
        class="-mr-2 w-48 rounded-[12px]"
        :placeholder="t('dashboard.admin.tenants.members.search')"
      />
    </template>
    <ul class="grid divide-y divide-border text-sm">
      <li v-for="row in rows" :key="row.userId" data-member-row class="flex items-center gap-3 px-2 py-2">
        <span class="min-w-0 flex-1 font-medium">{{ row.displayName }}</span>
        <span class="text-muted-foreground">
          {{ t(`dashboard.admin.tenants.members.relation.${row.relation}`, row.relation) }}
        </span>
      </li>
    </ul>
  </PanelCard>
</template>
