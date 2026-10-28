<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The range at a glance: the numbers the bot counted, how follows and chatters moved, and what stood out.
// Every number comes from the bot's own records for the range; nothing here is a sample.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useAnalyticsDays,
  useAnalyticsSummary,
  useStreamsInRange,
  type AnalyticsDay,
  type AnalyticsRange,
  type StreamRow,
} from '@/api/analytics';
import { type BoardPanel } from '@/components/layout/board';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import ChartPanel, { type ChartRow, type ChartSeries } from '@/components/layout/ChartPanel.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatStrip from '@/components/layout/StatStrip.vue';
import StatTile from '@/components/layout/StatTile.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { formatLength } from '@/lib/time';

import { averageSeconds, liveSeconds, perPeriod, topCategory } from './analyticsFacts';

type Highlight = {
  key: string;
  title: string;
  detail: string;
};

const props = defineProps<{ range: AnalyticsRange }>();

const { t, n, locale } = useI18n();
const { channelId } = useCurrentChannel();

const summary = useAnalyticsSummary(channelId, () => props.range);
const days = useAnalyticsDays(channelId, () => props.range);
const streams = useStreamsInRange(channelId, () => props.range);

const daysState = usePanelState(days, (list: AnalyticsDay[]) =>
  list.length === 0);
const highlightsState = usePanelState(streams);

const panels = computed<BoardPanel[]>(() => [
  { id: 'growth', label: t('dashboard.analytics.growth.title') },
  { id: 'highlights', label: t('dashboard.analytics.highlights.title') },
  { id: 'chatters', label: t('dashboard.analytics.chatters.title') },
]);

const period = computed(() => t(`dashboard.analytics.period.${props.range}`));
const rows = computed<StreamRow[]>(() => streams.data.value ?? []);
const live = computed(() => liveSeconds(rows.value));
const known = computed(() => summary.data.value ?? null);

function count(value: number | string | null | undefined): number | string {
  return value === null || value === undefined
    ? t('dashboard.analytics.kpi.none')
    : Number(value);
}

const perHour = computed<string | undefined>(() => {
  if (!known.value || live.value === 0)
    return undefined;

  const lines = Math.round(Number(known.value.totalMessages) / (live.value / 3600));

  return t('dashboard.analytics.kpi.perHour', { count: n(lines) });
});

const followSeries: ChartSeries[] = [
  {
    key: 'value',
    labelKey: 'dashboard.analytics.growth.series',
    color: 'chart-1',
  },
];
const chatterSeries: ChartSeries[] = [
  {
    key: 'value',
    labelKey: 'dashboard.analytics.chatters.series',
    color: 'chart-2',
  },
];

function perDay(field: 'newFollowers' | 'uniqueChatters'): ChartRow[] {
  const pick = (day: AnalyticsDay): number => Number(day[field]);

  return perPeriod(days.data.value ?? [], props.range, pick, locale.value);
}

const follows = computed(() => perDay('newFollowers'));
const chatters = computed(() => perDay('uniqueChatters'));

const growthTake = computed(() =>
  t('dashboard.analytics.growth.takeaway', {
    count: n(Number(known.value?.newFollowers ?? 0)),
    period: period.value,
  }));

const chattersTake = computed<string>(() => {
  const busiest = Math.max(0, ...chatters.value.map((row: ChartRow) => Number(row.value)));

  return t('dashboard.analytics.chatters.takeaway', { count: n(busiest) });
});

function length(seconds: number): string {
  return formatLength(seconds, locale.value);
}

const highlights = computed<Highlight[]>(() => {
  const lines: Highlight[] = [];
  const top = topCategory(rows.value);

  lines.push({
    key: 'streams',
    title: t('dashboard.analytics.highlights.streams'),
    detail: rows.value.length > 0
      ? t('dashboard.analytics.highlights.average', { length: length(averageSeconds(rows.value)) })
      : t('dashboard.analytics.highlights.noStreams'),
  });

  if (top) {
    lines.push({
      key: 'category',
      title: t('dashboard.analytics.highlights.category'),
      detail: t(
        'dashboard.analytics.highlights.categoryLine',
        {
          name: top.name,
          length: length(top.seconds),
          count: top.streams,
        },
        top.streams,
      ),
    });
  }

  if (known.value) {
    lines.push({
      key: 'commands',
      title: t('dashboard.analytics.highlights.commands'),
      detail: t('dashboard.analytics.highlights.commandsLine', { count: n(Number(known.value.commandsRun)) }),
    }, {
      key: 'rewards',
      title: t('dashboard.analytics.highlights.rewards'),
      detail: t('dashboard.analytics.highlights.rewardsLine', {
        count: n(Number(known.value.redemptionsCount)),
      }),
    }, {
      key: 'songs',
      title: t('dashboard.analytics.highlights.songs'),
      detail: t('dashboard.analytics.highlights.songsLine', {
        count: n(Number(known.value.songRequests)),
      }),
    });
  }

  return lines;
});
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <div class="flex-none px-4 pt-4 lg:px-6">
      <StatStrip data-analytics-kpis>
        <StatTile
          :label="t('dashboard.analytics.kpi.streams')"
          :value="rows.length"
          :context="t('dashboard.analytics.kpi.live', { length: length(live) })"
        />
        <StatTile
          :label="t('dashboard.analytics.kpi.chat')"
          :value="count(known?.totalMessages)"
          :context="perHour"
        />
        <StatTile :label="t('dashboard.analytics.kpi.follows')" :value="count(known?.newFollowers)" />
        <StatTile
          :label="t('dashboard.analytics.kpi.subs')"
          :value="count(known?.newSubscribers)"
          :context="t('dashboard.analytics.kpi.subsKinds')"
        />
        <StatTile :label="t('dashboard.analytics.kpi.bits')" :value="count(known?.bitsCheered)" />
        <StatTile :label="t('dashboard.analytics.kpi.peak')" :value="count(known?.peakViewers)" />
      </StatStrip>
    </div>

    <BoardGrid :panels="panels" :rows="2">
      <BoardCell panel="growth" :span="7">
        <ChartPanel
          data-analytics-growth
          :title="t('dashboard.analytics.growth.title')"
          :description="t('dashboard.analytics.growth.description')"
          kind="bar"
          :data="follows"
          x="period"
          :series="followSeries"
          :takeaway="growthTake"
          :state="daysState"
          :empty-text="t('dashboard.analytics.growth.empty')"
          :error-text="t('dashboard.analytics.error')"
          @retry="days.refetch()"
        />
      </BoardCell>
      <BoardCell panel="highlights" :span="5" :rows="2">
        <PanelCard
          data-analytics-highlights
          pad
          :title="t('dashboard.analytics.highlights.title')"
          :state="highlightsState"
          :error-text="t('dashboard.analytics.error')"
          @retry="streams.refetch()"
        >
          <RowGroup>
            <SummaryRow
              v-for="line in highlights"
              :key="line.key"
              :title="line.title"
              :detail="line.detail"
            />
          </RowGroup>
        </PanelCard>
      </BoardCell>
      <BoardCell panel="chatters" :span="7">
        <ChartPanel
          data-analytics-chatters
          :title="t('dashboard.analytics.chatters.title')"
          :description="t('dashboard.analytics.chatters.description')"
          kind="bar"
          :data="chatters"
          x="period"
          :series="chatterSeries"
          :takeaway="chattersTake"
          :state="daysState"
          :empty-text="t('dashboard.analytics.chatters.empty')"
          :error-text="t('dashboard.analytics.error')"
          @retry="days.refetch()"
        />
      </BoardCell>
    </BoardGrid>
  </div>
</template>
