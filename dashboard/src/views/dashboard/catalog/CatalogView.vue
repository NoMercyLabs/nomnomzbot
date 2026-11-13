<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Development only: every shared building block in each of its states (spec section 5, step 2).
// One section component per area lives in ./components; this page only owns the header and the tabs.
import { useI18n } from 'vue-i18n';

import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useTabbedPage, type TabSpec } from '@/composables/useTabbedPage';

import CatalogCharts from './components/CatalogCharts.vue';
import CatalogCodeEditor from './components/CatalogCodeEditor.vue';
import CatalogConnections from './components/CatalogConnections.vue';
import CatalogDialogs from './components/CatalogDialogs.vue';
import CatalogEditors from './components/CatalogEditors.vue';
import CatalogFeedback from './components/CatalogFeedback.vue';
import CatalogFilters from './components/CatalogFilters.vue';
import CatalogFlows from './components/CatalogFlows.vue';
import CatalogGuide from './components/CatalogGuide.vue';
import CatalogLayouts from './components/CatalogLayouts.vue';
import CatalogLists from './components/CatalogLists.vue';
import CatalogPages from './components/CatalogPages.vue';
import CatalogPanelStates from './components/CatalogPanelStates.vue';
import CatalogRows from './components/CatalogRows.vue';
import CatalogStats from './components/CatalogStats.vue';
import CatalogTemplate from './components/CatalogTemplate.vue';

const { t } = useI18n();

const VIEWS = ['blocks', 'states', 'layouts', 'feedback', 'rows', 'pages', 'guide', 'locked'] as const;

type CatalogTab = (typeof VIEWS)[number];

const specs = () => [
  {
    value: 'blocks',
    label: t('dashboard.catalog.tabs.blocks'),
  },
  {
    value: 'states',
    label: t('dashboard.catalog.tabs.states'),
  },
  {
    value: 'layouts',
    label: t('dashboard.catalog.tabs.layouts'),
  },
  {
    value: 'feedback',
    label: t('dashboard.catalog.tabs.feedback'),
  },
  {
    value: 'rows',
    label: t('dashboard.catalog.tabs.rows'),
  },
  {
    value: 'pages',
    label: t('dashboard.catalog.tabs.pages'),
  },
  {
    value: 'guide',
    label: t('dashboard.catalog.tabs.guide'),
  },
  {
    value: 'locked',
    label: t('dashboard.catalog.tabs.locked'),
    disabledReason: t('dashboard.catalog.lockedReason'),
  },
] as const satisfies readonly TabSpec<CatalogTab>[];

const { tabs, view } = useTabbedPage(VIEWS, specs);
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <PageHeader :title="t('dashboard.catalog.title')" :crumb="t('dashboard.catalog.crumb')">
      <template #status>
        <StatusBadge tone="live" :label="t('dashboard.catalog.status.live')" />
      </template>
      <template #meta>{{ t('dashboard.catalog.meta') }}</template>
      <template #actions>
        <Button variant="outline">{{ t('dashboard.catalog.outline') }}</Button>
        <Button>{{ t('dashboard.catalog.primary') }}</Button>
      </template>
      <template #bar><PageTabs :tabs="tabs" /></template>
    </PageHeader>

    <!-- Below md the page scrolls as one document; the body box scrolls on its own only from md up. -->
    <div class="min-h-0 flex-1 md:overflow-y-auto">
      <div class="grid gap-4 px-6 pt-4 pb-6 max-md:px-4">
        <template v-if="view === 'blocks'">
          <CatalogStats />
          <CatalogFilters />
          <CatalogDialogs />
          <CatalogCodeEditor />
          <CatalogFlows />
          <CatalogCharts />
          <CatalogTemplate />
          <CatalogLists />
        </template>
        <CatalogPanelStates v-else-if="view === 'states'" />
        <CatalogLayouts v-else-if="view === 'layouts'" />
        <template v-else-if="view === 'feedback'">
          <CatalogFeedback />
          <CatalogConnections />
        </template>
        <template v-else-if="view === 'rows'">
          <CatalogRows />
          <CatalogEditors />
        </template>
        <CatalogPages v-else-if="view === 'pages'" />
        <CatalogGuide v-else />
      </div>
    </div>
  </div>
</template>
