<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Analytics: how the channel did over a range the streamer picks; the range is remembered per browser.
import { useLocalStorage } from '@vueuse/core';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { ANALYTICS_RANGES, type AnalyticsRange } from '@/api/analytics';
import PageHeader from '@/components/layout/PageHeader.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';

import AnalyticsOverview from './AnalyticsOverview.vue';

const { t } = useI18n();

const range = useLocalStorage<AnalyticsRange>('nnz.analytics.range', 'r30');
const rangeOptions = computed(() =>
  (Object.keys(ANALYTICS_RANGES) as AnalyticsRange[]).map((value: AnalyticsRange) => ({
    value,
    label: t(`dashboard.analytics.range.${value}`),
  })));
</script>

<template>
  <PageHeader :title="$t('dashboard.analytics.title')" :crumb="$t('dashboard.analytics.crumb')">
    <template #actions>
      <SegmentedControl
        v-model="range"
        :label="$t('dashboard.analytics.range.label')"
        :options="rangeOptions"
      />
    </template>
  </PageHeader>
  <AnalyticsOverview :range="range" />
</template>
