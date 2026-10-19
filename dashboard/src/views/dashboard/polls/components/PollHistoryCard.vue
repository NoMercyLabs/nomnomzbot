<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One finished poll or prediction as a quiet card: the question, its type and date, the winner with its
// share, and short bars for every option. A status shows only when it changes how the result reads.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PollHistoryItem, PollHistoryOption } from '@/api/liveOps';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { useFormattedTime } from '@/composables/useTime';

import Trophy from '~icons/nnz/trophy';

import { showsStatus, statusTone, winnerOf } from '../pollHistory';

import PollBar from './PollBar.vue';

const props = defineProps<{ item: PollHistoryItem }>();

const { t, n } = useI18n();

const winner = computed(() => winnerOf(props.item));
const date = useFormattedTime(() => props.item.startedAt, 'date');
const statusLabel = computed(() =>
  t(`dashboard.polls.status.${props.item.status.toLowerCase()}`, props.item.status),
);

const meta = computed(() =>
  [t(`dashboard.polls.kind.${props.item.kind}`, props.item.kind), date.value].join(' · '),
);

function percentLabel(option: PollHistoryOption): string {
  return `${n(percentOf(option))}%`;
}

function percentOf(option: PollHistoryOption): number {
  return Number(option.sharePercent);
}
</script>

<template>
  <li data-history-card class="flex flex-col gap-3 rounded-lg border border-border bg-card p-3">
    <div class="flex flex-col gap-1">
      <div class="flex items-start justify-between gap-2">
        <span class="line-clamp-2 text-sm font-medium">{{ item.title }}</span>
        <StatusBadge
          v-if="showsStatus(item.status)"
          class="flex-none"
          :tone="statusTone(item.status)"
          :label="statusLabel"
        />
      </div>
      <span class="text-xs text-muted-foreground">
        {{ meta }}
      </span>
    </div>
    <p v-if="winner !== null" class="flex items-center gap-1 text-sm">
      <Trophy
        class="size-4 flex-none text-muted-foreground"
        role="img"
        :aria-label="t('dashboard.polls.winner')"
      />
      <span data-option-title class="min-w-0 flex-1 truncate font-medium">{{ winner.title }}</span>
      <span class="tabular-nums">{{ percentLabel(winner) }}</span>
    </p>
    <ul class="flex flex-col gap-1.5">
      <li
        v-for="option in item.options"
        :key="option.id"
        data-poll-option
        class="flex items-center gap-2 text-xs text-muted-foreground"
      >
        <span class="basis-2/5 flex-none break-words">{{ option.title }}</span>
        <PollBar
          class="flex-1"
          size="sm"
          :tone="option.isWinner ? 'strong' : 'muted'"
          :percent="percentOf(option)"
        />
        <span class="w-9 flex-none text-end tabular-nums">{{ percentLabel(option) }}</span>
      </li>
    </ul>
  </li>
</template>
