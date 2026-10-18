<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Analytics: how the channel did over a range the streamer picks; the range is remembered per browser.
// The tab sits in the address, so a link or the back button lands on the same view.
import { useLocalStorage } from '@vueuse/core';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { ANALYTICS_RANGES, type AnalyticsRange } from '@/api/analytics';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { useCan } from '@/composables/useCan';
import { usePageView } from '@/composables/usePageView';

import AnalyticsChat from './AnalyticsChat.vue';
import AnalyticsOverview from './AnalyticsOverview.vue';
import AnalyticsStreams from './AnalyticsStreams.vue';

const { t } = useI18n();
const { can } = useCan();

const VIEWS = ['overview', 'streams', 'chat'] as const satisfies readonly string[];
const view = usePageView(VIEWS);
const views = computed<PageTab[]>(() =>
  VIEWS.map((value) => ({ value, label: t(`dashboard.analytics.views.${value}`) })));

const range = useLocalStorage<AnalyticsRange>('nnz.analytics.range', 'r30');
const rangeOptions = computed(() =>
  (Object.keys(ANALYTICS_RANGES) as AnalyticsRange[]).map((value: AnalyticsRange) => ({
    value,
    label: t(`dashboard.analytics.range.${value}`),
  })));
</script>

<template>
  <PageHeader
    v-if="can('analytics:read')"
    :title="$t('dashboard.analytics.title')"
    :crumb="$t('dashboard.analytics.crumb')"
  >
    <template #actions>
      <SegmentedControl
        v-model="range"
        :label="$t('dashboard.analytics.range.label')"
        :options="rangeOptions"
      />
    </template>
    <template #bar><PageTabs :tabs="views" /></template>
  </PageHeader>
  <template v-if="can('analytics:read')">
    <AnalyticsStreams v-if="view === 'streams'" :range="range" />
    <AnalyticsChat v-else-if="view === 'chat'" :range="range" />
    <AnalyticsOverview v-else :range="range" />
  </template>
</template>
