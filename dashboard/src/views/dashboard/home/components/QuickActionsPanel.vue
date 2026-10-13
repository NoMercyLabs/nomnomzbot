<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Four rows of what the streamer may want next: the ad break (with Snooze), the song queue,
// the timers and a running poll or prediction. Each row reads its own source, so one failing
// source shows its own line and the others still render. Nothing here is invented: a source
// with no data says so.
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import {
  usePolls,
  usePredictions,
  useTimers,
  type Poll,
  type Prediction,
  type Timer,
} from '@/api/liveOps'
import { useMusicQueue, type QueueItem } from '@/api/music'
import { useAdSchedule, useSnoozeAd, type AdSchedule } from '@/api/stream'
import Countdown from '@/components/layout/Countdown.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import BarChart from '~icons/nnz/bar-chart'
import Megaphone from '~icons/nnz/megaphone'
import NoteDouble from '~icons/nnz/note-double'
import Timer24 from '~icons/nnz/timer'

type Running = { title: string; kind: 'poll' | 'prediction'; target: number | null }

const { t } = useI18n()
const router = useRouter()
const { channelId } = useCurrentChannel()

const adQuery = useAdSchedule(channelId)
const snooze = useSnoozeAd(channelId)
const queueQuery = useMusicQueue(channelId)
const timersQuery = useTimers(channelId)
const pollsQuery = usePolls(channelId)
const predictionsQuery = usePredictions(channelId)

const MS: number = 1000

// An ad break the server did not schedule has no time; zero means none.
const ad: ComputedRef<AdSchedule | null> = computed(() => {
  const schedule: AdSchedule | null = adQuery.data.value ?? null

  return schedule !== null && Number(schedule.nextAdAt) > 0 ? schedule : null
})
const snoozesLeft = computed(() => Number(ad.value?.snoozeCount ?? 0))
const adMinutes = computed(() => Math.round(Number(ad.value?.duration ?? 0) / 60))
const adDetail: ComputedRef<string> = computed(() =>
  [
    t('dashboard.home.quick.adLength', { minutes: adMinutes.value }),
    t('dashboard.home.quick.snoozesLeft', snoozesLeft.value),
  ].join(' · '),
)

const queue: ComputedRef<QueueItem[]> = computed(() => queueQuery.data.value?.queue ?? [])
const senders: ComputedRef<string[]> = computed(() => [
  ...new Set(
    queue.value
      .map((item: QueueItem): string | null => item.requestedBy ?? null)
      .filter((n): n is string => n !== null),
  ),
])
const queueDetail: ComputedRef<string> = computed(() => {
  const waiting: string = t('dashboard.home.quick.waiting', { count: queue.value.length })
  const everyoneNamed =
    senders.value.length === 1 &&
    queue.value.every((item: QueueItem) => item.requestedBy === senders.value[0])

  return everyoneNamed
    ? `${waiting} · ${t('dashboard.home.quick.allSentBy', { name: senders.value[0] })}`
    : waiting
})

// The server sends no next-fire time, so the next timer is the enabled one whose last fire plus interval comes first.
function dueAt(timer: Timer): number {
  return (
    new Date(timer.lastFiredAt ?? timer.createdAt).getTime() +
    Number(timer.intervalMinutes) * 60 * MS
  )
}

const enabledTimers: ComputedRef<Timer[]> = computed(() =>
  (timersQuery.data.value ?? []).filter((timer: Timer) => timer.isEnabled),
)
const nextTimer: ComputedRef<Timer | null> = computed(
  () => [...enabledTimers.value].sort((a: Timer, b: Timer) => dueAt(a) - dueAt(b))[0] ?? null,
)

const running: ComputedRef<Running | null> = computed(() => {
  const poll: Poll | undefined = (pollsQuery.data.value ?? []).find(
    (p: Poll) => p.status.toUpperCase() === 'ACTIVE',
  )

  if (poll !== undefined) {
    return {
      title: poll.title,
      kind: 'poll',
      target: new Date(poll.startedAt).getTime() + Number(poll.duration) * MS,
    }
  }

  const prediction: Prediction | undefined = (predictionsQuery.data.value ?? []).find(
    (p: Prediction) => ['ACTIVE', 'LOCKED'].includes(p.status.toUpperCase()),
  )

  if (prediction === undefined) return null
  const open = prediction.status.toUpperCase() === 'ACTIVE'

  return {
    title: prediction.title,
    kind: 'prediction',
    target: open
      ? new Date(prediction.createdAt).getTime() + Number(prediction.predictionWindow) * MS
      : null,
  }
})
const pollsPending = computed(() => pollsQuery.isPending.value || predictionsQuery.isPending.value)
const pollsFailed = computed(() => pollsQuery.isError.value || predictionsQuery.isError.value)

const musicLinked: ComputedRef<boolean> = computed(() => router.hasRoute('dashboard-music'))
</script>

<template>
  <PanelCard :title="t('dashboard.home.quick.title')">
    <RowGroup>
      <div data-quick-row="ad">
        <Skeleton v-if="adQuery.isPending.value" class="h-11 rounded-[8px]" />
        <SummaryRow
          v-else-if="adQuery.isError.value"
          :title="t('dashboard.home.quick.adTitle')"
          :detail="t('dashboard.home.quick.adError')"
        >
          <template #leading><Megaphone class="size-5 text-muted-foreground" /></template>
        </SummaryRow>
        <SummaryRow
          v-else-if="ad === null"
          :title="t('dashboard.home.quick.adTitle')"
          :detail="t('dashboard.home.quick.adNone')"
        >
          <template #leading><Megaphone class="size-5 text-muted-foreground" /></template>
        </SummaryRow>
        <SummaryRow v-else :title="t('dashboard.home.quick.adTitle')" :detail="adDetail">
          <template #leading><Megaphone class="size-5 text-muted-foreground" /></template>
          <template #end>
            <span class="text-sm font-medium">
              <Countdown
                :target="Number(ad.nextAdAt) * MS"
                :label="t('dashboard.home.quick.adTitle')"
              />
            </span>
          </template>
          <template #trailing>
            <Button
              variant="outline"
              size="sm"
              :disabled="snoozesLeft <= 0 || snooze.isPending.value"
              @click="snooze.mutate()"
            >
              {{ t('dashboard.home.quick.snooze', { minutes: 5 }) }}
            </Button>
          </template>
        </SummaryRow>
      </div>

      <div data-quick-row="music">
        <Skeleton v-if="queueQuery.isPending.value" class="h-11 rounded-[8px]" />
        <SummaryRow
          v-else-if="queueQuery.isError.value"
          :title="t('dashboard.home.quick.musicTitle')"
          :detail="t('dashboard.home.quick.musicError')"
        >
          <template #leading><NoteDouble class="size-5 text-muted-foreground" /></template>
        </SummaryRow>
        <SummaryRow
          v-else
          :title="t('dashboard.home.quick.musicTitle')"
          :detail="queue.length > 0 ? queueDetail : t('dashboard.home.quick.musicEmpty')"
        >
          <template #leading><NoteDouble class="size-5 text-muted-foreground" /></template>
          <template v-if="musicLinked && queue.length > 0" #trailing>
            <Button variant="ghost" size="sm" as-child>
              <RouterLink :to="{ name: 'dashboard-music' }">{{
                t('dashboard.home.quick.openQueue')
              }}</RouterLink>
            </Button>
          </template>
        </SummaryRow>
      </div>

      <div data-quick-row="timers">
        <Skeleton v-if="timersQuery.isPending.value" class="h-11 rounded-[8px]" />
        <SummaryRow
          v-else-if="timersQuery.isError.value"
          :title="t('dashboard.home.quick.timersTitle')"
          :detail="t('dashboard.home.quick.timersError')"
        >
          <template #leading><Timer24 class="size-5 text-muted-foreground" /></template>
        </SummaryRow>
        <SummaryRow
          v-else-if="nextTimer === null"
          :title="t('dashboard.home.quick.timersTitle')"
          :detail="t('dashboard.home.quick.timersNone')"
        >
          <template #leading><Timer24 class="size-5 text-muted-foreground" /></template>
        </SummaryRow>
        <SummaryRow
          v-else
          :title="t('dashboard.home.quick.timersOn', enabledTimers.length)"
          :detail="t('dashboard.home.quick.nextTimer', { name: nextTimer.name })"
        >
          <template #leading><Timer24 class="size-5 text-muted-foreground" /></template>
        </SummaryRow>
      </div>

      <div data-quick-row="poll">
        <Skeleton v-if="pollsPending" class="h-11 rounded-[8px]" />
        <SummaryRow
          v-else-if="running !== null"
          :title="running.title"
          :detail="
            t(
              running.kind === 'poll'
                ? 'dashboard.home.quick.pollRunning'
                : 'dashboard.home.quick.predictionRunning',
            )
          "
        >
          <template #leading><BarChart class="size-5 text-muted-foreground" /></template>
          <template #end>
            <span class="text-sm font-medium">
              <Countdown
                v-if="running.target !== null"
                :target="running.target"
                :label="running.title"
              />
              <template v-else>{{ t('dashboard.home.quick.locked') }}</template>
            </span>
          </template>
        </SummaryRow>
        <SummaryRow
          v-else
          :title="t('dashboard.home.quick.pollTitle')"
          :detail="
            pollsFailed ? t('dashboard.home.quick.pollError') : t('dashboard.home.quick.pollNone')
          "
        >
          <template #leading><BarChart class="size-5 text-muted-foreground" /></template>
        </SummaryRow>
      </div>
    </RowGroup>
  </PanelCard>
</template>
