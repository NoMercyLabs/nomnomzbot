<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Per-tenant error budget for the last 24 hours, from real outbound webhook delivery outcomes.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminPageParams } from '@/api/adminActivity';
import { useAdminErrorBudget, type AdminTenantErrorBudget } from '@/api/adminErrorBudget';
import { ApiError } from '@/api/errors';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './components/AdminPager.vue';
import ErrorBudgetTable from './components/ErrorBudgetTable.vue';

const PAGE_SIZE: number = 25;

const { t, locale } = useI18n();

const page = ref(1);

const params = computed<AdminPageParams>(() => ({ page: page.value, take: PAGE_SIZE }));

const budgets = useAdminErrorBudget(params);

const rows = computed<AdminTenantErrorBudget[]>(() => budgets.data.value?.data ?? []);
const total = computed(() => Number(budgets.data.value?.total ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

// The target is a stated policy threshold the server sends on every row, never a number made up here.
const target = computed<string>(() => {
  const first = rows.value[0];

  if (!first)
    return '';

  const format = new Intl.NumberFormat(locale.value, { style: 'percent', maximumFractionDigits: 2 });

  return format.format(Number(first.targetSuccessRate));
});

const panelState = usePanelState(budgets, (data) =>
  (data.data?.length ?? 0) === 0);
const listError = computed<string>(() =>
  budgets.error.value instanceof ApiError && budgets.error.value.status === 403
    ? t('dashboard.admin.errorBudget.list.forbidden')
    : t('dashboard.admin.errorBudget.list.error'),
);
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.errorBudget.title')" :crumb="$t('dashboard.admin.crumb')">
    <template #status>
      <span v-if="budgets.data.value" class="text-sm text-muted-foreground tabular-nums">
        {{ $t("dashboard.admin.errorBudget.count", { count: total }) }}
      </span>
    </template>
  </PageHeader>
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <p class="text-sm text-muted-foreground">{{ $t("dashboard.admin.errorBudget.restricted") }}</p>
    <PanelCard
      tier="lead"
      class="min-h-80 min-w-0 flex-1"
      :title="$t('dashboard.admin.errorBudget.list.title')"
      :description="$t('dashboard.admin.errorBudget.list.window', { target })"
      :state="panelState"
      :empty-text="$t('dashboard.admin.errorBudget.list.empty')"
      :error-text="listError"
      @retry="budgets.refetch()"
    >
      <ErrorBudgetTable :rows="rows" />
      <template #footer>
        <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
      </template>
    </PanelCard>
  </div>
</template>
