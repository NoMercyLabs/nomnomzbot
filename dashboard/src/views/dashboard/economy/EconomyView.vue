<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Economy page: the channel's currency, how viewers earn it, and the savings jars.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import PageBody from '@/components/layout/PageBody.vue';
import PageCreateButton from '@/components/layout/PageCreateButton.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import { useTabbedPage, type TabSpec } from '@/composables/useTabbedPage';

import BalancesPanel from './components/BalancesPanel.vue';
import CatalogPanel from './components/CatalogPanel.vue';
import CurrencyPanel from './components/CurrencyPanel.vue';
import EarningRules from './components/EarningRules.vue';
import LeaderboardsPanel from './components/LeaderboardsPanel.vue';
import SavingsJars from './components/SavingsJars.vue';

const { t } = useI18n();

const addingRule = ref(false);
const addingJar = ref(false);
const addingItem = ref(false);
const addingBoard = ref(false);

const VIEWS = ['currency', 'balances', 'rules', 'jars', 'catalog', 'leaderboards'] as const;

type EconomyView = (typeof VIEWS)[number];

// Each tab needs the read key of its own endpoint; its New button needs the key that creates.
const specs = (): readonly TabSpec<EconomyView>[] => [
  {
    value: 'currency',
    label: t('dashboard.economy.tabs.currency'),
    read: 'economy:config:read',
  },
  {
    value: 'balances',
    label: t('dashboard.economy.tabs.balances'),
    read: 'economy:accounts:read',
  },
  {
    value: 'rules',
    label: t('dashboard.economy.tabs.rules'),
    read: 'economy:earning-rules:read',
    action: {
      label: t('dashboard.economy.rules.new'),
      write: 'economy:earning-rules:write',
      run: (): void => {
        addingRule.value = true;
      },
    },
  },
  {
    value: 'jars',
    label: t('dashboard.economy.tabs.jars'),
    read: 'economy:jars:read',
    action: {
      label: t('dashboard.economy.jars.new'),
      write: 'economy:jars:create',
      run: (): void => {
        addingJar.value = true;
      },
    },
  },
  {
    value: 'catalog',
    label: t('dashboard.economy.tabs.catalog'),
    read: 'economy:catalog:read',
    action: {
      label: t('dashboard.economy.catalog.new'),
      write: 'economy:catalog:create',
      run: (): void => {
        addingItem.value = true;
      },
    },
  },
  {
    value: 'leaderboards',
    label: t('dashboard.economy.tabs.leaderboards'),
    read: 'economy:leaderboards:config:read',
    action: {
      label: t('dashboard.economy.leaderboards.new'),
      write: 'economy:leaderboards:config:write',
      run: (): void => {
        addingBoard.value = true;
      },
    },
  },
];

const { tabs, view, action, canAct } = useTabbedPage(VIEWS, specs);
</script>

<template>
  <PageHeader :title="$t('dashboard.economy.title')" :crumb="$t('dashboard.economy.crumb')">
    <template #actions>
      <PageCreateButton
        v-if="canAct && action"
        :key="view"
        :label="action.label"
        @create="action.run()"
      />
    </template>
    <template #bar>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <PageBody>
    <CurrencyPanel v-if="view === 'currency'" />
    <BalancesPanel v-else-if="view === 'balances'" />
    <EarningRules v-else-if="view === 'rules'" v-model:adding="addingRule" />
    <SavingsJars v-else-if="view === 'jars'" v-model:adding="addingJar" />
    <CatalogPanel v-else-if="view === 'catalog'" v-model:adding="addingItem" />
    <LeaderboardsPanel v-else-if="view === 'leaderboards'" v-model:adding="addingBoard" />
  </PageBody>
</template>
