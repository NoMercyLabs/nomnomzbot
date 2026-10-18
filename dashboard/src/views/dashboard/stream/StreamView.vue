<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePageView } from '@/composables/usePageView';
import { useTime } from '@/composables/useTime';

import CommercialPanel from './CommercialPanel.vue';
import GoLiveChecklist from './GoLiveChecklist.vue';
import { formatDuration } from './formatDuration';
import PlatformRows from './PlatformRows.vue';
import RaidRulesPanel from './RaidRulesPanel.vue';
import SchedulePanel from './SchedulePanel.vue';
import StreamInfoPanel from './StreamInfoPanel.vue';

const { t } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const VIEWS = ['overview', 'schedule', 'raids', 'ads'] as const satisfies readonly string[];
const view = usePageView(VIEWS);
const viewKeys = {
  overview: ['stream:read', 'integration:read'],
  schedule: ['live-ops:schedule:read'],
  raids: ['live-ops:raids:read'],
  ads: ['live-ops:ads:read'],
} as const satisfies Record<(typeof VIEWS)[number], readonly string[]>;
const { can } = useCan();
// A tab shows when the person holds the read key of at least one panel on it.
const allowed = computed<readonly string[]>(() =>
  VIEWS.filter((value) => viewKeys[value].some(can)));
const tabs = computed<PageTab[]>(() =>
  allowed.value.map((value) => ({ value, label: t(`dashboard.stream.views.${value}`) })));
// A typed ?view= the person may not open falls back to the first tab they can.
const shown = computed(() =>
  allowed.value.includes(view.value)
    ? view.value
    : allowed.value[0]);
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

  <div v-if="shown === 'schedule'" data-page-body class="min-h-0 flex-1 overflow-y-auto p-4"><SchedulePanel /></div>
  <div v-else-if="shown === 'raids'" data-page-body class="min-h-0 flex-1 overflow-y-auto p-4"><RaidRulesPanel /></div>
  <div v-else-if="shown === 'ads'" data-page-body class="min-h-0 flex-1 overflow-y-auto p-4"><CommercialPanel /></div>
  <BoardGrid v-else-if="shown === 'overview'" :panels="panels" :rows="2">
    <BoardCell v-if="can('stream:read')" panel="info" :span="8" :rows="2"><StreamInfoPanel /></BoardCell>
    <BoardCell v-if="can('integration:read')" panel="platforms" :span="4"><PlatformRows /></BoardCell>
    <BoardCell v-if="can('stream:read')" panel="checklist" :span="4"><GoLiveChecklist /></BoardCell>
  </BoardGrid>
</template>
