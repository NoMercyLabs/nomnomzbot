<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The filter bars and the search field. The two choices are remembered across reloads.
import { useLocalStorage } from '@vueuse/core';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import FilterBar from '@/components/layout/FilterBar.vue';
import type { FilterOption } from '@/components/layout/filterOption';
import SearchField from '@/components/layout/SearchField.vue';

const { t } = useI18n();

const stateOptions: FilterOption[] = ['all', 'on', 'off'].map((key) => ({
  value: key,
  label: t(`dashboard.catalog.filter.${key}`),
}));
const sinceOptions: FilterOption[] = ['day', 'week', 'month', 'quarter', 'year'].map(
  (key) => ({
    value: key,
    label: t(`dashboard.catalog.filter.${key}`),
  }),
);

const stateFilter = useLocalStorage<string>('nnz.catalog.state', 'all');
const sinceFilter = useLocalStorage<string>('nnz.catalog.since', 'week');
const query = ref('');
</script>

<template>
  <section data-catalog="filters" class="grid gap-4">
    <FilterBar
      v-model="stateFilter"
      :label="t('dashboard.catalog.filter.state')"
      :options="stateOptions"
    />
    <SearchField v-model="query" :placeholder="t('dashboard.catalog.search')" />
    <FilterBar
      v-model="sinceFilter"
      :label="t('dashboard.catalog.filter.since')"
      :options="sinceOptions"
    />
  </section>
</template>
