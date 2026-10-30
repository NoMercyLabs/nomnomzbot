<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One limited resource: its name, "used of limit", and a bar. The bar is neutral until the resource is near
// its limit; a full one turns destructive, because the next save of that kind is refused.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { LimitLevel } from '../limitRows';
import { formatAmount, type LimitUnit } from '../limitUnits';

const props = defineProps<{
  label: string;
  unit: LimitUnit;
  used: number;
  limit: number;
  percent: number;
  level: LimitLevel;
  detail?: string;
}>();

const { locale, t } = useI18n();

const figures = computed(() => {
  const used = formatAmount(props.used, props.unit, locale.value);

  if (props.level === 'excluded')
    return t('dashboard.settings.resourceLimits.meter.excluded');

  return props.level === 'unlimited'
    ? t('dashboard.settings.resourceLimits.meter.unlimited', { used })
    : t('dashboard.settings.resourceLimits.meter.of', { used, limit: formatAmount(props.limit, props.unit, locale.value) });
});

const hasBar = computed(() => props.level !== 'unlimited' && props.level !== 'excluded');

const barClass = computed(() => {
  if (props.level === 'full')
    return 'bg-destructive';

  return props.level === 'near'
    ? 'bg-foreground'
    : 'bg-foreground/50';
});
</script>

<template>
  <div data-limit-row :data-level="level" class="grid gap-1.5 px-3 py-2.5">
    <div class="flex flex-wrap items-baseline gap-x-3 gap-y-0.5">
      <span class="min-w-0 flex-1 basis-48 text-sm font-medium">{{ label }}</span>
      <span class="text-sm tabular-nums" :class="level === 'full' ? 'text-destructive' : 'text-muted-foreground'">{{
        figures
      }}</span>
    </div>
    <div
      v-if="hasBar"
      role="progressbar"
      :aria-label="label"
      aria-valuemin="0"
      aria-valuemax="100"
      :aria-valuenow="percent"
      class="h-1.5 overflow-hidden rounded-full bg-background"
    >
      <div class="h-full rounded-full" :class="barClass" :style="{ width: `${percent}%` }" />
    </div>
    <span v-if="detail" class="text-sm text-muted-foreground">{{ detail }}</span>
  </div>
</template>
