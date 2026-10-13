<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The stream page: header with the live state, then the stream info beside where it goes and what to check.
import { useIntervalFn, useNow } from '@vueuse/core'
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import { useStreamHistory, useStreamInfo, type StreamInfo } from '@/api/stream'
import BoardCell from '@/components/layout/BoardCell.vue'
import BoardGrid from '@/components/layout/BoardGrid.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

import GoLiveChecklist from './GoLiveChecklist.vue'
import { formatDuration } from './formatDuration'
import PlatformRows from './PlatformRows.vue'
import StreamInfoPanel from './StreamInfoPanel.vue'

const { t, locale } = useI18n()
const { channelId } = useCurrentChannel()
const infoQuery = useStreamInfo(channelId)
const historyQuery = useStreamHistory(channelId)
const now = useNow({ scheduler: (tick: () => void) => useIntervalFn(tick, 30_000) })

const info: ComputedRef<StreamInfo | null> = computed(() => infoQuery.data.value ?? null)
const live = computed(() => info.value?.isLive === true)

const date = (value: string): string =>
  new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium' }).format(new Date(value))

const meta: ComputedRef<string> = computed(() => {
  const current = info.value

  if (current === null) return ''
  if (current.isLive && current.startedAt)
    return t('dashboard.stream.liveMeta', {
      time: formatDuration((now.value.getTime() - new Date(current.startedAt).getTime()) / 1000),
      category: current.gameName,
    })
  const last = historyQuery.data.value?.[0]

  // A stream the bot never closed has no end or length: the header gives only its date.
  if (last?.endedAt && last.durationSeconds != null)
    return t('dashboard.stream.lastStream', {
      date: date(last.endedAt),
      length: formatDuration(Number(last.durationSeconds)),
    })
  const lastDate = last?.startedAt ?? current.lastStreamedAt

  if (lastDate) return t('dashboard.stream.lastStreamDate', { date: date(lastDate) })

  return t('dashboard.stream.noStreamYet')
})
</script>

<template>
  <PageHeader :title="t('dashboard.stream.title')">
    <template #status>
      <StatusBadge
        :tone="live ? 'ok' : 'off'"
        :label="live ? t('dashboard.stream.live') : t('dashboard.stream.offline')"
      />
    </template>
    <template v-if="meta" #meta>{{ meta }}</template>
  </PageHeader>

  <BoardGrid :rows="2">
    <BoardCell :span="8" :rows="2"><StreamInfoPanel /></BoardCell>
    <BoardCell :span="4"><PlatformRows /></BoardCell>
    <BoardCell :span="4"><GoLiveChecklist /></BoardCell>
  </BoardGrid>
</template>
