<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Economy page: the channel's currency, how viewers earn it, and the savings jars.
import Plus from '~icons/nnz/add';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { usePageView } from '@/composables/usePageView';
import { keysOf } from '@/lib/array';

import BalancesPanel from './components/BalancesPanel.vue';
import CatalogPanel from './components/CatalogPanel.vue';
import CurrencyPanel from './components/CurrencyPanel.vue';
import EarningRules from './components/EarningRules.vue';
import LeaderboardsPanel from './components/LeaderboardsPanel.vue';
import SavingsJars from './components/SavingsJars.vue';

const { t } = useI18n();

const { can } = useCan();

// Each tab needs the read key of its own endpoint; its New button needs the key that creates.
const tabKeys = {
  currency: { read: 'economy:config:read', create: null },
  balances: { read: 'economy:accounts:read', create: null },
  rules: { read: 'economy:earning-rules:read', create: 'economy:earning-rules:write' },
  jars: { read: 'economy:jars:read', create: 'economy:jars:create' },
  catalog: { read: 'economy:catalog:read', create: 'economy:catalog:create' },
  leaderboards: {
    read: 'economy:leaderboards:config:read',
    create: 'economy:leaderboards:config:write',
  },
} as const satisfies Record<string, { read: string; create: string | null }>;
const VIEWS = keysOf(tabKeys);

type EconomyView = (typeof VIEWS)[number];
const allTabs = computed<PageTab<EconomyView>[]>(() => [
  { value: 'currency', label: t('dashboard.economy.tabs.currency') },
  { value: 'balances', label: t('dashboard.economy.tabs.balances') },
  { value: 'rules', label: t('dashboard.economy.tabs.rules') },
  { value: 'jars', label: t('dashboard.economy.tabs.jars') },
  { value: 'catalog', label: t('dashboard.economy.tabs.catalog') },
  { value: 'leaderboards', label: t('dashboard.economy.tabs.leaderboards') },
]);

function canRead(tab: PageTab<EconomyView>): boolean {
  return can(tabKeys[tab.value].read);
}

function canCreate(tab: EconomyView): boolean {
  const key = tabKeys[tab].create;

  return key !== null && can(key);
}

const tabs = computed(() => allTabs.value.filter(canRead));
const view = usePageView(VIEWS, 'view', () =>
  tabs.value.map((tab) => tab.value));

const addingRule = ref(false);
const addingJar = ref(false);
const addingItem = ref(false);
const addingBoard = ref(false);
</script>

<template>
  <PageHeader :title="$t('dashboard.economy.title')" :crumb="$t('dashboard.economy.crumb')">
    <template #actions>
      <Button
        v-if="view === 'rules' && canCreate('rules')"
        type="button"
        variant="outline"
        size="sm"
        @click="addingRule = true"
      >
        <Plus aria-hidden="true" />
        {{ $t('dashboard.economy.rules.new') }}
      </Button>
      <Button
        v-if="view === 'jars' && canCreate('jars')"
        type="button"
        variant="outline"
        size="sm"
        @click="addingJar = true"
      >
        <Plus aria-hidden="true" />
        {{ $t('dashboard.economy.jars.new') }}
      </Button>
      <Button
        v-if="view === 'catalog' && canCreate('catalog')"
        type="button"
        variant="outline"
        size="sm"
        @click="addingItem = true"
      >
        <Plus aria-hidden="true" />
        {{ $t('dashboard.economy.catalog.new') }}
      </Button>
      <Button
        v-if="view === 'leaderboards' && canCreate('leaderboards')"
        type="button"
        variant="outline"
        size="sm"
        @click="addingBoard = true"
      >
        <Plus aria-hidden="true" />
        {{ $t('dashboard.economy.leaderboards.new') }}
      </Button>
    </template>
    <template #bar>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <div data-page-body class="min-h-0 flex-1 overflow-y-auto px-6 pt-4 pb-6">
    <CurrencyPanel v-if="view === 'currency'" />
    <BalancesPanel v-else-if="view === 'balances'" />
    <EarningRules v-else-if="view === 'rules'" v-model:adding="addingRule" />
    <SavingsJars v-else-if="view === 'jars'" v-model:adding="addingJar" />
    <CatalogPanel v-else-if="view === 'catalog'" v-model:adding="addingItem" />
    <LeaderboardsPanel v-else-if="view === 'leaderboards'" v-model:adding="addingBoard" />
  </div>
</template>
