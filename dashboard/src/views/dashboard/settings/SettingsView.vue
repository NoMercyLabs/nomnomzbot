<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The settings page. Channel: a list of settings beside the open one (only Readability is built so far).
// This device: what stays in this browser, with a link back into the Channel list.
import BookOpenText from '~icons/nnz/book-open';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { usePageView } from '@/composables/usePageView';

import DeviceView from './DeviceView.vue';
import ReadabilityView from './ReadabilityView.vue';

const { t } = useI18n();

const tabs = computed<PageTab[]>(() => [
  { value: 'channel', label: t('dashboard.settings.tabs.channel') },
  { value: 'device', label: t('dashboard.settings.tabs.device') },
]);
const view = usePageView(() => tabs.value.map((tab: PageTab) => tab.value));
</script>

<template>
  <PageHeader :title="$t('dashboard.settings.title')" :crumb="$t('dashboard.settings.crumb')">
    <template #tabs><PageTabs :tabs="tabs" /></template>
  </PageHeader>

  <ListDetail v-if="view === 'channel'" show-detail>
    <template #list>
      <PanelCard :title="$t('dashboard.settings.nav.you')" tier="quiet">
        <RowGroup>
          <SummaryRow pick selected :title="$t('dashboard.settings.nav.readability')">
            <template #leading><BookOpenText class="size-4 shrink-0" /></template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>
    </template>
    <template #detail><ReadabilityView /></template>
  </ListDetail>

  <div v-else class="px-6 pt-4 pb-6">
    <DeviceView @open-readability="view = 'channel'" />
  </div>
</template>
