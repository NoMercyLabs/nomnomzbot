<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// Every poll and prediction the channel ran: Twitch polls and predictions from the event journal, and the
// bot's own chat polls. Each row shows its choices with votes and share, and marks the winner.
import { useLocalStorage } from '@vueuse/core';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePollHistory, type PollHistoryItem, type PollHistoryOption } from '@/api/liveOps';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import Trophy from '~icons/nnz/trophy';

import { filterByKind, historyKinds, statusTone, type HistoryKind } from './pollHistory';

const { t, n, locale } = useI18n();
const { channelId } = useCurrentChannel();

const history = usePollHistory(channelId);
const kind = useLocalStorage<HistoryKind>('nnz.polls.kind', 'all');

const kindOptions = computed(() =>
  historyKinds.map((value) => ({
    value,
    label: t(`dashboard.polls.kinds.${value}`),
  })),
);
const rows = computed(() => filterByKind(history.data.value ?? [], kind.value));
const state = usePanelState(history, () =>
  rows.value.length === 0);

function when(timestamp: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(timestamp));
}

function statusLabel(item: PollHistoryItem): string {
  const status = item.status.toLowerCase();

  return t(`dashboard.polls.status.${status}`, item.status);
}

function optionDetail(item: PollHistoryItem, option: PollHistoryOption): string {
  const votes = Number(option.votes);
  const points = Number(option.channelPoints);
  const parts = [
    t('dashboard.polls.votes', { count: n(votes) }, votes),
    `${n(Number(option.sharePercent))}%`,
  ];

  if (item.kind === 'prediction' && points > 0) {
    parts.push(t('dashboard.polls.points', { count: n(points) }, points));
  }

  return parts.join(' · ');
}
</script>

<template>
  <PageHeader :title="t('dashboard.polls.title')" />
  <PanelCard
    tier="lead"
    :title="t('dashboard.polls.listTitle')"
    :state="state"
    :empty-text="t('dashboard.polls.empty')"
    :error-text="t('dashboard.polls.error')"
    @retry="history.refetch()"
  >
    <template #actions>
      <SegmentedControl
        v-model="kind"
        :label="t('dashboard.polls.filterLabel')"
        :options="kindOptions"
      />
    </template>
    <ul class="divide-y">
      <li
        v-for="item in rows"
        :key="`${item.kind}:${item.id}`"
        data-poll-row
        class="flex flex-col gap-2 py-3 first:pt-0 last:pb-0"
      >
        <div class="flex flex-wrap items-baseline gap-x-3 gap-y-1">
          <span class="font-medium">{{ item.title }}</span>
          <span class="text-sm text-muted-foreground">{{
            t(`dashboard.polls.kind.${item.kind}`, item.kind)
          }}</span>
          <span class="text-sm text-muted-foreground tabular-nums">{{ when(item.startedAt) }}</span>
          <StatusBadge class="ms-auto" :tone="statusTone(item.status)" :label="statusLabel(item)" />
        </div>
        <ul class="flex flex-col gap-1">
          <li
            v-for="option in item.options"
            :key="option.id"
            data-poll-option
            :data-winner="option.isWinner || undefined"
            class="grid grid-cols-[minmax(0,12rem)_1fr_auto] items-center gap-3 text-sm"
          >
            <span
              class="flex min-w-0 items-center gap-1"
              :class="option.isWinner ? 'font-medium' : 'text-muted-foreground'"
            >
              <Trophy
                v-if="option.isWinner"
                class="size-4 flex-none"
                role="img"
                :aria-label="t('dashboard.polls.winner')"
              />
              <span data-option-title class="truncate">{{ option.title }}</span>
            </span>
            <span class="h-1.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
              <span
                class="block h-full rounded-full"
                :class="option.isWinner ? 'bg-primary' : 'bg-muted-foreground/40'"
                :style="{ width: `${Number(option.sharePercent)}%` }"
              />
            </span>
            <span class="text-muted-foreground tabular-nums">{{ optionDetail(item, option) }}</span>
          </li>
        </ul>
      </li>
    </ul>
  </PanelCard>
</template>
