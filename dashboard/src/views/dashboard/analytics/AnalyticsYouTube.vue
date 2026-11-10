<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->

<script setup lang="ts">
// The YouTube tab: the channel's own YouTube numbers per day, newest first, read with the streamer's permission.
// A channel that is not connected, or that did not allow analytics yet, gets the reason and the way to fix it.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';

import { useYouTubeAnalytics, type AnalyticsRange, type YouTubeAnalytics, type YouTubeDay } from '@/api/analytics';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
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

const props = defineProps<{ range: AnalyticsRange }>();

const { t, n } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const page = 'dashboard.analytics.youtube';

const youtube = useYouTubeAnalytics(channelId, () => props.range);
const blocked = computed(() => {
  const access = youtube.data.value?.access;

  return access === 'not_connected' || access === 'not_allowed'
    ? access
    : null;
});
const days = computed<YouTubeDay[]>(() => [...(youtube.data.value?.days ?? [])].reverse());
const showsRevenue = computed(() => youtube.data.value?.revenueAvailable === true);
const state = usePanelState(youtube, (answer: YouTubeAnalytics | null) =>
  answer?.access === 'allowed' && (answer.days ?? []).length === 0);

const sum = (pick: (day: YouTubeDay) => number | string | null | undefined): number =>
  days.value.reduce((total, day) => total + Number(pick(day) ?? 0), 0);

const takeaway = computed(() =>
  t(`${page}.takeaway`, {
    views: n(sum((day) => day.views)),
    time: time.duration(sum((day) => day.minutesWatched) * 60),
    subscribers: n(sum((day) => day.subscribersGained) - sum((day) => day.subscribersLost)),
  }));

function revenue(day: YouTubeDay): string {
  return day.estimatedRevenue === null || day.estimatedRevenue === undefined
    ? t(`${page}.noRevenue`)
    : n(Number(day.estimatedRevenue), { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col p-4 lg:p-6">
    <PanelCard
      data-analytics-youtube
      :title="t(`${page}.title`)"
      :description="t(`${page}.description`)"
      :state="state"
      :empty-text="t(`${page}.empty`)"
      :error-text="t('dashboard.analytics.error')"
      @retry="youtube.refetch()"
    >
      <div v-if="blocked" data-youtube-blocked class="grid justify-items-start gap-3 px-2 py-3">
        <p class="text-sm text-foreground">{{ t(`${page}.blocked.${blocked}`) }}</p>
        <Button as-child variant="outline" size="sm">
          <RouterLink :to="{ name: 'dashboard-settings-integrations' }">
            {{ t(`${page}.blocked.open`) }}
          </RouterLink>
        </Button>
      </div>
      <template v-else>
        <p class="px-2 pt-2 pb-3 text-sm text-foreground">{{ takeaway }}</p>
        <p v-if="!showsRevenue" data-youtube-no-revenue class="px-2 pb-3 text-sm text-muted-foreground">
          {{ t(`${page}.notMonetised`) }}
        </p>
        <Table>
          <TableHeader>
            <TableRow class="hover:bg-transparent">
              <TableHead>{{ t(`${page}.columns.day`) }}</TableHead>
              <TableHead class="text-right">{{ t(`${page}.columns.views`) }}</TableHead>
              <TableHead class="text-right">{{ t(`${page}.columns.watched`) }}</TableHead>
              <TableHead class="text-right max-md:hidden">{{ t(`${page}.columns.gained`) }}</TableHead>
              <TableHead class="text-right max-md:hidden">{{ t(`${page}.columns.lost`) }}</TableHead>
              <TableHead v-if="showsRevenue" class="text-right">{{ t(`${page}.columns.revenue`) }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="day in days" :key="day.day" data-youtube-day>
              <TableCell class="font-medium">{{ time.weekdayDate(day.day) }}</TableCell>
              <TableCell class="text-right tabular-nums">{{ n(Number(day.views)) }}</TableCell>
              <TableCell class="text-right tabular-nums">
                {{ time.duration(Number(day.minutesWatched) * 60) }}
              </TableCell>
              <TableCell class="text-right tabular-nums max-md:hidden">
                {{ n(Number(day.subscribersGained)) }}
              </TableCell>
              <TableCell class="text-right tabular-nums max-md:hidden">
                {{ n(Number(day.subscribersLost)) }}
              </TableCell>
              <TableCell v-if="showsRevenue" class="text-right tabular-nums">{{ revenue(day) }}</TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </template>
    </PanelCard>
  </div>
</template>
