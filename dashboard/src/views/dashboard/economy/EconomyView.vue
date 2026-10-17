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
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue';
import { Button } from '@/components/ui/button';
import { usePageView } from '@/composables/usePageView';

import CurrencyPanel from './components/CurrencyPanel.vue';
import EarningRules from './components/EarningRules.vue';
import SavingsJars from './components/SavingsJars.vue';

const { t } = useI18n();

const tabs = computed<PageTab[]>(() => [
  { value: 'currency', label: t('dashboard.economy.tabs.currency') },
  { value: 'rules', label: t('dashboard.economy.tabs.rules') },
  { value: 'jars', label: t('dashboard.economy.tabs.jars') },
]);
const view = usePageView(['currency', 'rules', 'jars']);

const addingRule = ref(false);
const addingJar = ref(false);
</script>

<template>
  <PageHeader :title="$t('dashboard.economy.title')" :crumb="$t('dashboard.economy.crumb')">
    <template #actions>
      <Button
        v-if="view === 'rules'"
        type="button"
        variant="outline"
        size="sm"
        @click="addingRule = true"
      >
        <Plus aria-hidden="true" />
        {{ $t('dashboard.economy.rules.new') }}
      </Button>
      <Button
        v-if="view === 'jars'"
        type="button"
        variant="outline"
        size="sm"
        @click="addingJar = true"
      >
        <Plus aria-hidden="true" />
        {{ $t('dashboard.economy.jars.new') }}
      </Button>
    </template>
    <template #bar>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <div data-page-body class="min-h-0 flex-1 overflow-y-auto px-6 pt-4 pb-6">
    <CurrencyPanel v-if="view === 'currency'" />
    <EarningRules v-else-if="view === 'rules'" v-model:adding="addingRule" />
    <SavingsJars v-else v-model:adding="addingJar" />
  </div>
</template>
