<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The tenant console: pick a tenant on the left (?tenant=), manage it on the right. Hosting a tenant for a
// third party is the restricted saas mode, so the page says so.
import { refDebounced } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useAdminTenant, useAdminTenants, type AdminTenant, type AdminTenantsParams } from '@/api/admin';
import DetailPane from '@/components/layout/DetailPane.vue';
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import { usePanelState } from '@/composables/usePanelState';

import ChannelsTable from './components/ChannelsTable.vue';
import TenantActions from './components/TenantActions.vue';
import TenantLimitsPanel from './components/TenantLimitsPanel.vue';
import TenantMembersPanel from './components/TenantMembersPanel.vue';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();

const search = ref('');
const typed = refDebounced(search, 300);
const params = computed<AdminTenantsParams>(() => ({
  search: typed.value.trim(),
  page: 1,
  take: 50,
}));

const tenants = useAdminTenants(params);
const rows = computed<AdminTenant[]>(() => tenants.data.value?.data ?? []);
const listState = usePanelState(tenants, (page) =>
  (page.data?.length ?? 0) === 0);

const selectedId = computed<string | null>(() =>
  typeof route.query.tenant === 'string'
    ? route.query.tenant
    : null,
);
const detail = useAdminTenant(selectedId);
const tenant = computed(() => detail.data.value ?? null);
const detailState = usePanelState(detail, (data) =>
  data === null);

function pick(row: AdminTenant): void {
  void router.push({ query: { ...route.query, tenant: row.id } });
}

const facts = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.admin.tenants.facts.owner'), value: tenant.value?.ownerDisplayName },
  { label: t('dashboard.admin.tenants.facts.status'), value: tenant.value?.status },
  { label: t('dashboard.admin.tenants.facts.tier'), value: tenant.value?.billingTierKey },
  { label: t('dashboard.admin.tenants.facts.mode'), value: tenant.value?.deploymentMode },
  { label: t('dashboard.admin.tenants.facts.members'), value: Number(tenant.value?.membershipCount ?? 0) },
  { label: t('dashboard.admin.tenants.facts.suspendedReason'), value: tenant.value?.suspendedReason },
]);
</script>

<template>
  <PageHeader :title="t('dashboard.admin.tenants.title')" :crumb="t('dashboard.admin.crumb')" />
  <ListDetail :show-detail="selectedId !== null">
    <template #list>
      <PanelCard
        tier="lead"
        :title="t('dashboard.admin.list.title')"
        :state="listState"
        :empty-text="t('dashboard.admin.list.empty')"
        :error-text="t('dashboard.admin.list.error')"
        @retry="tenants.refetch()"
      >
        <template #actions>
          <SearchField
            v-model="search"
            class="-mr-2 w-40 rounded-[12px]"
            :placeholder="t('dashboard.admin.search')"
          />
        </template>
        <ChannelsTable :rows="rows" :selected-id="selectedId ?? ''" @pick="pick" />
      </PanelCard>
    </template>
    <template #detail>
      <PanelCard
        v-if="selectedId === null"
        :title="t('dashboard.admin.tenants.title')"
        state="empty"
        :empty-text="t('dashboard.admin.tenants.pick')"
      />
      <DetailPane v-else>
        <template #header>
          <h2 class="text-lg font-semibold">{{ tenant?.name }}</h2>
          <p data-saas-restricted class="text-sm text-muted-foreground">
            {{ t('dashboard.admin.tenants.restricted') }}
          </p>
        </template>
        <PanelCard
          :title="t('dashboard.admin.tenants.facts.title')"
          pad
          :state="detailState"
          :error-text="t('dashboard.admin.tenants.error')"
          @retry="detail.refetch()"
        >
          <div v-if="tenant" class="grid gap-4">
            <KeyValueList :items="facts" />
            <TenantActions :tenant="tenant" />
          </div>
        </PanelCard>
        <TenantMembersPanel :tenant-id="selectedId" />
        <TenantLimitsPanel :tenant-id="selectedId" />
      </DetailPane>
    </template>
  </ListDetail>
</template>
