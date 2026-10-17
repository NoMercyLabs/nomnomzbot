<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform admin's EventSub registry: which topics each channel is subscribed to, in what real state,
// and when each was last confirmed. The bot pages the channels; the page only reads.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useAdminEventSubHealth,
  type AdminEventSubTenantHealth,
  type AdminPageParams,
} from '@/api/adminActivity';
import { ApiError } from '@/api/errors';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './components/AdminPager.vue';
import EventSubHealthTable from './components/EventSubHealthTable.vue';

const PAGE_SIZE: number = 25;

const { t } = useI18n();

const page = ref(1);

const params = computed<AdminPageParams>(() => ({ page: page.value, take: PAGE_SIZE }));

const health = useAdminEventSubHealth(params);

const rows = computed<AdminEventSubTenantHealth[]>(() => health.data.value?.data ?? []);
const total = computed(() => Number(health.data.value?.total ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

const panelState = usePanelState(health, (data) =>
  (data.data?.length ?? 0) === 0);
const listError = computed<string>(() =>
  health.error.value instanceof ApiError && health.error.value.status === 403
    ? t('dashboard.admin.eventsubHealth.list.forbidden')
    : t('dashboard.admin.eventsubHealth.list.error'),
);
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.eventsubHealth.title')" :crumb="$t('dashboard.admin.crumb')">
    <template #status>
      <span v-if="health.data.value" class="text-sm text-muted-foreground tabular-nums">
        {{ $t('dashboard.admin.eventsubHealth.count', { count: total }) }}
      </span>
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <PanelCard
      tier="lead"
      class="min-h-80 min-w-0 flex-1"
      :title="$t('dashboard.admin.eventsubHealth.list.title')"
      :description="$t('dashboard.admin.eventsubHealth.list.description')"
      :state="panelState"
      :empty-text="$t('dashboard.admin.eventsubHealth.list.empty')"
      :error-text="listError"
      @retry="health.refetch()"
    >
      <EventSubHealthTable :rows="rows" />
      <template #footer>
        <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
      </template>
    </PanelCard>
  </div>
</template>
