<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The stream page: header with the live state, then the stream info beside where it goes and what to check.
import { useIntervalFn, useNow } from '@vueuse/core';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useStreamHistory, useStreamInfo, type StreamInfo } from '@/api/stream';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import type { BoardPanel } from '@/components/layout/board';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useTabbedPage, type TabSpec } from '@/composables/useTabbedPage';
import { useTime } from '@/composables/useTime';

import ClipsPanel from './ClipsPanel.vue';
import CommercialPanel from './CommercialPanel.vue';
import GoLiveChecklist from './GoLiveChecklist.vue';
import { formatDuration } from './formatDuration';
import PlatformRows from './PlatformRows.vue';
import RaidNowPanel from './RaidNowPanel.vue';
import RaidRulesPanel from './RaidRulesPanel.vue';
import SchedulePanel from './SchedulePanel.vue';
import StreamInfoPanel from './StreamInfoPanel.vue';

const { t } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const VIEWS = ['overview', 'schedule', 'raids', 'ads', 'clips'] as const;

type StreamTab = (typeof VIEWS)[number];

const { can } = useCan();
const tabLabel = (value: StreamTab): string => t(`dashboard.stream.views.${value}`);
// The Overview tab shows when the person holds the read key of at least one of its panels, so it has no single key.
const overviewOpen = computed<boolean>(() => can('stream:read') || can('integration:read'));
const specs = (): readonly TabSpec<StreamTab>[] => [
  ...(overviewOpen.value
    ? [{ value: 'overview', label: tabLabel('overview') } as const]
    : []),
  {
    value: 'schedule',
    label: tabLabel('schedule'),
    read: 'live-ops:schedule:read',
  },
  {
    value: 'raids',
    label: tabLabel('raids'),
    read: 'live-ops:raids:read',
  },
  {
    value: 'ads',
    label: tabLabel('ads'),
    read: 'live-ops:ads:read',
  },
  {
    value: 'clips',
    label: tabLabel('clips'),
    read: 'live-ops:clips:write',
  },
];
// A typed ?view= the person may not open falls back to the first tab they can.
const { tabs, view: shown } = useTabbedPage(VIEWS, specs);
const infoQuery = useStreamInfo(channelId);
const historyQuery = useStreamHistory(channelId);
const now = useNow({ scheduler: (tick: () => void) => useIntervalFn(tick, 30_000) });

const info = computed<StreamInfo | null>(() => infoQuery.data.value ?? null);
const live = computed(() => info.value?.isLive === true);

const date = (value: string): string => time.date(value);

const meta = computed<string>(() => {
  const current = info.value;

  if (current === null)
    return '';

  if (current.isLive && current.startedAt)
    return t('dashboard.stream.liveMeta', {
      time: formatDuration((now.value.getTime() - new Date(current.startedAt).getTime()) / 1000),
      category: current.gameName,
    });

  const last = historyQuery.data.value?.[0];

  // A stream the bot never closed has no end or length: the header gives only its date.
  if (last?.endedAt && last.durationSeconds != null)
    return t('dashboard.stream.lastStream', {
      date: date(last.endedAt),
      length: formatDuration(Number(last.durationSeconds)),
    });

  const lastDate = last?.startedAt ?? current.lastStreamedAt;

  if (lastDate)
    return t('dashboard.stream.lastStreamDate', { date: date(lastDate) });

  return t('dashboard.stream.noStreamYet');
});

const panels = computed(() => [
  { id: 'info', label: t('dashboard.stream.info.title') },
  { id: 'platforms', label: t('dashboard.stream.platforms.title') },
  { id: 'checklist', label: t('dashboard.stream.checklist.title') },
] as const satisfies readonly BoardPanel[]);
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
    <template v-if="tabs.length > 1" #bar><PageTabs :tabs="tabs" /></template>
  </PageHeader>

  <PageBody v-if="shown === 'schedule'"><SchedulePanel /></PageBody>
  <PageBody v-else-if="shown === 'raids'"><RaidNowPanel /><RaidRulesPanel /></PageBody>
  <PageBody v-else-if="shown === 'ads'"><CommercialPanel /></PageBody>
  <PageBody v-else-if="shown === 'clips'"><ClipsPanel /></PageBody>
  <BoardGrid v-else-if="shown === 'overview'" :panels="panels" :rows="2">
    <BoardCell v-if="can('stream:read')" panel="info" :span="8" :rows="2"><StreamInfoPanel /></BoardCell>
    <BoardCell v-if="can('integration:read')" panel="platforms" :span="4"><PlatformRows /></BoardCell>
    <BoardCell v-if="can('stream:read')" panel="checklist" :span="4"><GoLiveChecklist /></BoardCell>
  </BoardGrid>
</template>
