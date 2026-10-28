<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// "When chat is busy" (mockup 11-analytics #chat): one row per weekday, one cell per hour, darker for more chat.
// The grid is a picture for the eye; the sentence above it and the lists beside it carry the same facts in words.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ChatHourCell } from '@/api/analytics';

import { HOURS, WEEK, dayName, heatLevel, hourName, linesAt } from './chatFacts';

const props = defineProps<{ cells: readonly ChatHourCell[]; label: string }>();

const { t, n, locale } = useI18n();

const LEVELS = ['bg-muted', 'bg-foreground/30', 'bg-foreground/60', 'bg-foreground'] as const;

const most = computed(() =>
  Math.max(0, ...props.cells.map((cell) =>
    Number(cell.messages))));

function tone(day: number, hour: number): string {
  return LEVELS[heatLevel(linesAt(props.cells, day, hour), most.value)];
}

function cellTitle(day: number, hour: number): string {
  return t('dashboard.analytics.chat.cell', {
    day: dayName(day, 'long', locale.value),
    from: hourName(hour, locale.value),
    to: hourName((hour + 1) % 24, locale.value),
    count: n(linesAt(props.cells, day, hour)),
  });
}
</script>

<template>
  <div data-chat-heat class="grid gap-3">
    <div
      role="img"
      :aria-label="label"
      class="grid grid-cols-[auto_repeat(24,minmax(0,1fr))] items-center gap-0.5"
    >
      <span />
      <span v-for="hour in HOURS" :key="`h${hour}`" class="text-center text-xs text-muted-foreground">
        <template v-if="hour % 6 === 0">{{ hourName(hour, locale, true) }}</template>
      </span>
      <template v-for="day in WEEK" :key="day">
        <span class="pr-2 text-xs text-muted-foreground">{{ dayName(day, 'short', locale) }}</span>
        <span
          v-for="hour in HOURS"
          :key="`${day}-${hour}`"
          data-heat-cell
          class="aspect-square rounded-[3px]"
          :class="tone(day, hour)"
          :title="cellTitle(day, hour)"
        />
      </template>
    </div>
    <div class="flex items-center justify-end gap-2 text-xs text-muted-foreground" aria-hidden="true">
      {{ $t('dashboard.analytics.chat.fewer') }}
      <span v-for="level in LEVELS" :key="level" class="size-3 rounded-[3px]" :class="level" />
      {{ $t('dashboard.analytics.chat.more') }}
    </div>
  </div>
</template>
