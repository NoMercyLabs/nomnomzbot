<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Chat tab (mockup 11-analytics #chat): when chat is busy, by weekday and hour, in the channel's time zone.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChatHours, type AnalyticsRange, type ChatHourCell, type ChatHours } from '@/api/analytics';
import BarList, { type BarItem } from '@/components/layout/BarList.vue';
import { type BoardPanel } from '@/components/layout/board';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import ChatHeatGrid from './ChatHeatGrid.vue';
import { busiestCell, dayName, dayTotals, hourName, topHours, type DayTotal, type HourTotal } from './chatFacts';

const props = defineProps<{ range: AnalyticsRange }>();

const { t, n, locale } = useI18n();
const { channelId } = useCurrentChannel();
const page = 'dashboard.analytics.chat';

const hours = useChatHours(channelId, () =>
  props.range);
const cells = computed<ChatHourCell[]>(() => hours.data.value?.cells ?? []);
const state = usePanelState(hours, (data: ChatHours | null) =>
  (data?.cells.length ?? 0) === 0);

const panels = computed<BoardPanel[]>(() => [
  { id: 'heat', label: t(`${page}.heat.title`) },
  { id: 'busy', label: t(`${page}.busy.title`) },
]);

function span(hour: number): string {
  return t(`${page}.span`, { from: hourName(hour, locale.value), to: hourName((hour + 1) % 24, locale.value) });
}

const heatTake = computed(() => {
  const busiest = busiestCell(cells.value);

  if (!busiest)
    return '';

  return t(`${page}.heat.takeaway`, {
    day: dayName(Number(busiest.dayOfWeek), 'long', locale.value),
    span: span(Number(busiest.hour)),
  });
});

const zone = computed(() =>
  t(`${page}.zone`, { zone: hours.data.value?.timeZone ?? 'UTC' }));

const days = computed<BarItem[]>(() =>
  dayTotals(cells.value).map((total: DayTotal) => ({
    key: String(total.day),
    label: dayName(total.day, 'long', locale.value),
    detail: total.busiestHour === null
      ? t(`${page}.busy.none`)
      : t(`${page}.busy.peak`, { hour: hourName(total.busiestHour, locale.value) }),
    value: total.lines,
  })));

const quietDays = computed(() =>
  days.value.filter((item: BarItem) =>
    item.value === 0).length);

const busyTake = computed(() => {
  const top = [...days.value].sort((a: BarItem, b: BarItem) => b.value - a.value)[0];

  if (!top)
    return '';

  const most = t(`${page}.busy.takeaway`, { day: top.label, count: n(top.value) });

  if (quietDays.value === 0)
    return most;

  return `${most} ${t(`${page}.busy.quiet`, { count: quietDays.value }, quietDays.value)}`;
});

const peakHours = computed<BarItem[]>(() =>
  topHours(cells.value, 7).map((total: HourTotal) => ({
    key: String(total.hour),
    label: span(total.hour),
    detail: n(total.share, { style: 'percent' }),
    value: total.lines,
  })));
</script>

<template>
  <BoardGrid :panels="panels" :rows="1">
    <BoardCell panel="heat" :span="7">
      <PanelCard
        data-analytics-heat
        pad
        :title="t(`${page}.heat.title`)"
        :description="zone"
        :state="state"
        :empty-text="t(`${page}.empty`)"
        :error-text="t('dashboard.analytics.error')"
        @retry="hours.refetch()"
      >
        <div class="flex flex-col gap-3">
          <p class="text-sm text-foreground">{{ heatTake }}</p>
          <ChatHeatGrid :cells="cells" :label="heatTake" />
        </div>
      </PanelCard>
    </BoardCell>
    <BoardCell panel="busy" :span="5">
      <PanelCard
        data-analytics-busy
        pad
        :title="t(`${page}.busy.title`)"
        :description="t(`${page}.busy.description`)"
        :state="state"
        :empty-text="t(`${page}.empty`)"
        :error-text="t('dashboard.analytics.error')"
        @retry="hours.refetch()"
      >
        <div class="flex flex-col gap-4">
          <p class="text-sm text-foreground">{{ busyTake }}</p>
          <section data-analytics-days class="grid gap-3">
            <h3 class="text-sm font-medium text-muted-foreground">{{ t(`${page}.busy.byDay`) }}</h3>
            <BarList :items="days" />
          </section>
          <section data-analytics-hours class="grid gap-3">
            <h3 class="text-sm font-medium text-muted-foreground">{{ t(`${page}.busy.byHour`) }}</h3>
            <BarList :items="peakHours" />
          </section>
        </div>
      </PanelCard>
    </BoardCell>
  </BoardGrid>
</template>
