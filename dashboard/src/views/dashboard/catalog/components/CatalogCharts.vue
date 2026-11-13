<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The four chart kinds on invented numbers: they are never shown anywhere else.
import { useI18n } from 'vue-i18n';

import ChartPanel, { type ChartRow, type ChartSeries } from '@/components/layout/ChartPanel.vue';

const { t } = useI18n();

const lineDemo: ChartRow[] = [1200, 1500, 1450, 2100, 2600, 2500, 3100, 3400].map(
  (lines, day) => ({
    day: new Date(2026, 0, day + 1),
    lines,
  }),
);
const monthDemo: ChartRow[] = [
  {
    month: 'Sep',
    returning: 36,
    fresh: 50,
  },
  {
    month: 'Oct',
    returning: 43,
    fresh: 27,
  },
  {
    month: 'Nov',
    returning: 41,
    fresh: 41,
  },
  {
    month: 'Dec',
    returning: 49,
    fresh: 45,
  },
  {
    month: 'Jan',
    returning: 35,
    fresh: 12,
  },
];
const lineSeries: ChartSeries[] = [
  {
    key: 'lines',
    labelKey: 'dashboard.catalog.chart.line.title',
    color: 'chart-1',
  },
];
const monthSeries: ChartSeries[] = [
  {
    key: 'returning',
    labelKey: 'dashboard.catalog.chart.series.returning',
    color: 'chart-1',
  },
  {
    key: 'fresh',
    labelKey: 'dashboard.catalog.chart.series.fresh',
    color: 'chart-3',
  },
];
</script>

<template>
  <section data-catalog="charts" class="grid gap-4">
    <ChartPanel
      kind="line"
      :title="t('dashboard.catalog.chart.line.title')"
      :takeaway="t('dashboard.catalog.chart.line.takeaway')"
      :data="lineDemo"
      x="day"
      :series="lineSeries"
    />
    <ChartPanel
      kind="area"
      :title="t('dashboard.catalog.chart.area.title')"
      :takeaway="t('dashboard.catalog.chart.area.takeaway')"
      :data="lineDemo"
      x="day"
      :series="lineSeries"
    />
    <ChartPanel
      kind="bar"
      :title="t('dashboard.catalog.chart.bar.title')"
      :takeaway="t('dashboard.catalog.chart.bar.takeaway')"
      :data="monthDemo"
      x="month"
      :series="monthSeries"
    />
    <ChartPanel
      kind="groupedBar"
      :title="t('dashboard.catalog.chart.groupedBar.title')"
      :takeaway="t('dashboard.catalog.chart.groupedBar.takeaway')"
      :data="monthDemo"
      x="month"
      :series="monthSeries"
    />
  </section>
</template>
