<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Per-tenant error budget for the last 24 hours, from real outbound webhook delivery outcomes.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminPageParams } from '@/api/adminActivity';
import { useAdminErrorBudget, type AdminTenantErrorBudget } from '@/api/adminErrorBudget';
import { ApiError } from '@/api/errors';
import { usePanelState } from '@/composables/usePanelState';

import AdminCollectionPage from './components/AdminCollectionPage.vue';
import ErrorBudgetTable from './components/ErrorBudgetTable.vue';

const PAGE_SIZE: number = 25;

const { t, locale } = useI18n();

const page = ref(1);

const params = computed(() =>
  ({ page: page.value, take: PAGE_SIZE }) as const satisfies AdminPageParams);

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

const windowText = computed<string>(() =>
  target.value === ''
    ? t('dashboard.admin.errorBudget.list.windowOnly')
    : t('dashboard.admin.errorBudget.list.window', { target: target.value }));

const count = computed<string | undefined>(() =>
  budgets.data.value
    ? t('dashboard.admin.errorBudget.count', { count: total.value })
    : undefined);

const panelState = usePanelState(budgets, (data) =>
  (data.data?.length ?? 0) === 0);
const listError = computed<string>(() =>
  budgets.error.value instanceof ApiError && budgets.error.value.status === 403
    ? t('dashboard.admin.errorBudget.list.forbidden')
    : t('dashboard.admin.errorBudget.list.error'),
);
</script>

<template>
  <AdminCollectionPage
    :title="$t('dashboard.admin.errorBudget.title')"
    :count="count"
    :page="page"
    :pages="pages"
    :state="panelState"
    :list-title="$t('dashboard.admin.errorBudget.list.title')"
    :description="windowText"
    :empty-text="$t('dashboard.admin.errorBudget.list.empty')"
    :error-text="listError"
    @previous="page -= 1"
    @next="page += 1"
    @retry="budgets.refetch()"
  >
    <template #notice>
      <p class="text-sm text-muted-foreground max-md:px-4">{{ $t("dashboard.admin.errorBudget.restricted") }}</p>
    </template>
    <ErrorBudgetTable :rows="rows" />
  </AdminCollectionPage>
</template>
