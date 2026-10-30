<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One titled group of limits. A limit is a meter; a per-message cap is a plain value, because there is
// nothing to use up. The name of a known limit is a plain word; an unknown one shows the name the bot gave it.
import { useI18n } from 'vue-i18n';

import RowGroup from '@/components/layout/RowGroup.vue';

import type { LimitRow } from '../limitRows';
import { formatAmount } from '../limitUnits';

import LimitMeter from './LimitMeter.vue';
import LimitValue from './LimitValue.vue';

defineProps<{
  group: string;
  title: string;
  description: string;
  rows: readonly LimitRow[];
  perMessage?: boolean;
  showBaseline?: boolean;
}>();

const { locale, t, te } = useI18n();

function labelOf(row: LimitRow): string {
  const path = `dashboard.settings.resourceLimits.limits.names.${row.key}`;

  return te(path)
    ? t(path)
    : row.label;
}

function capOf(row: LimitRow): string {
  if (row.level === 'excluded')
    return t('dashboard.settings.resourceLimits.meter.excluded');

  return row.level === 'unlimited'
    ? t('dashboard.settings.resourceLimits.limits.noCap')
    : t(
        'dashboard.settings.resourceLimits.limits.charactersPerMessage',
        { n: formatAmount(row.limit, row.unit, locale.value) },
        row.limit,
      );
}

function baselineOf(row: LimitRow): string {
  return t('dashboard.settings.resourceLimits.limits.baseline', {
    value: formatAmount(row.baseline, row.unit, locale.value),
  });
}
</script>

<template>
  <section :data-group="group" class="grid gap-2">
    <h3 class="text-sm font-medium">{{ title }}</h3>
    <p class="text-sm text-muted-foreground">{{ description }}</p>
    <RowGroup>
      <template v-for="row in rows" :key="row.key">
        <LimitValue v-if="perMessage" :label="labelOf(row)" :value="capOf(row)" />
        <LimitMeter
          v-else
          :label="labelOf(row)"
          :unit="row.unit"
          :used="row.used"
          :limit="row.limit"
          :percent="row.percent"
          :level="row.level"
          :detail="showBaseline ? baselineOf(row) : undefined"
        />
      </template>
    </RowGroup>
  </section>
</template>
