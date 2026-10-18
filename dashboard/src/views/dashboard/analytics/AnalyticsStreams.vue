<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Streams tab (mockup 11-analytics #streams): every stream in the range, newest first, with what it brought.
// A phone has no room for every column, so the started cell carries the hidden ones as a second line.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useStreamsInRange, type AnalyticsRange, type StreamRow } from '@/api/analytics';
import PanelCard from '@/components/layout/PanelCard.vue';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

import { averageSeconds } from './analyticsFacts';

const props = defineProps<{ range: AnalyticsRange }>();

const { t, n } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const page = 'dashboard.analytics.streams';

const streams = useStreamsInRange(channelId, () => props.range);
const rows = computed<StreamRow[]>(() => streams.data.value ?? []);
const state = usePanelState(streams, (list: StreamRow[]) =>
  list.length === 0);

function started(row: StreamRow): string {
  return row.startedAt
    ? time.weekdayDate(row.startedAt)
    : t(`${page}.unknown`);
}

function startedAt(row: StreamRow): string {
  return time.time(row.startedAt);
}

function length(row: StreamRow): string {
  return row.durationSeconds === null || row.durationSeconds === undefined
    ? t(`${page}.unknown`)
    : time.duration(Number(row.durationSeconds));
}

function peak(row: StreamRow): string {
  return row.peakViewers === null || row.peakViewers === undefined
    ? t(`${page}.unknown`)
    : n(Number(row.peakViewers));
}

function brought(row: StreamRow): string {
  return t(`${page}.brought`, {
    chatters: n(Number(row.uniqueChatters)),
    follows: n(Number(row.newFollowers)),
  });
}

const takeaway = computed(() => {
  const average = t(`${page}.average`, { length: time.duration(averageSeconds(rows.value)) });
  const busiest = [...rows.value].sort((a: StreamRow, b: StreamRow) =>
    Number(b.totalMessages) - Number(a.totalMessages))[0];

  if (!busiest || Number(busiest.totalMessages) === 0)
    return average;

  return `${average} ${t(`${page}.busiest`, { date: started(busiest), count: n(Number(busiest.totalMessages)) })}`;
});
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col p-4 lg:p-6">
    <PanelCard
      data-analytics-streams
      :title="t(`${page}.title`)"
      :description="t(`${page}.count`, { count: rows.length }, rows.length)"
      :state="state"
      :empty-text="t(`${page}.empty`)"
      :error-text="t('dashboard.analytics.error')"
      @retry="streams.refetch()"
    >
      <p class="px-2 pt-2 pb-3 text-sm text-foreground">{{ takeaway }}</p>
      <Table>
        <TableHeader>
          <TableRow class="hover:bg-transparent">
            <TableHead>{{ t(`${page}.columns.started`) }}</TableHead>
            <TableHead>{{ t(`${page}.columns.length`) }}</TableHead>
            <TableHead class="max-md:hidden">{{ t(`${page}.columns.category`) }}</TableHead>
            <TableHead class="text-right">{{ t(`${page}.columns.lines`) }}</TableHead>
            <TableHead class="text-right max-md:hidden">{{ t(`${page}.columns.chatters`) }}</TableHead>
            <TableHead class="text-right max-md:hidden">{{ t(`${page}.columns.follows`) }}</TableHead>
            <TableHead class="text-right max-md:hidden">{{ t(`${page}.columns.peak`) }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="row in rows" :key="row.streamId" data-stream-row>
            <TableCell>
              <span class="font-medium">{{ started(row) }}</span>
              <span class="ml-1.5 text-muted-foreground">{{ startedAt(row) }}</span>
              <span class="block text-muted-foreground md:hidden">{{ row.gameName }}</span>
              <span class="block text-muted-foreground md:hidden">{{ brought(row) }}</span>
            </TableCell>
            <TableCell class="tabular-nums">{{ length(row) }}</TableCell>
            <TableCell class="max-md:hidden">{{ row.gameName }}</TableCell>
            <TableCell class="text-right tabular-nums">{{ n(Number(row.totalMessages)) }}</TableCell>
            <TableCell class="text-right tabular-nums max-md:hidden">{{ n(Number(row.uniqueChatters)) }}</TableCell>
            <TableCell class="text-right tabular-nums max-md:hidden">{{ n(Number(row.newFollowers)) }}</TableCell>
            <TableCell class="text-right tabular-nums max-md:hidden">{{ peak(row) }}</TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </PanelCard>
  </div>
</template>
