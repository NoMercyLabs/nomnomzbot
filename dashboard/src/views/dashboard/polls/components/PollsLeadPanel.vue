<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one lead panel of the page. While a poll or prediction runs it shows that one with large bars and
// the single action it needs next (End, Lock or Resolve). With nothing running it offers Start a poll as
// the one primary action, with Prediction beside it as an outline sibling. The accent lives only here.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useEndPoll,
  useLockPrediction,
  type Poll,
  type Prediction,
} from '@/api/liveOps';
import Countdown from '@/components/layout/Countdown.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { pollBars, predictionBars, type LeadBar } from '../pollHistory';

import PollBar from './PollBar.vue';

const props = defineProps<{
  poll: Poll | null;
  prediction: Prediction | null;
}>();

const emit = defineEmits<{
  startPoll: [];
  startPrediction: [];
  openPrediction: [];
}>();

const MS = 1000;

const { t, n } = useI18n();
const { channelId } = useCurrentChannel();
const endPoll = useEndPoll(channelId);
const lockPrediction = useLockPrediction(channelId);
const { can } = useCan();

// What the running poll or prediction needs next is a change; a person who cannot change it only watches.
const canChange = computed(() =>
  props.poll !== null
    ? can('live-ops:polls:write')
    : can('live-ops:predictions:write'),
);

const running = computed(() => props.poll !== null || props.prediction !== null);
const title = computed(() => props.poll?.title ?? props.prediction?.title ?? '');
const bars = computed<LeadBar[]>(() => {
  if (props.poll !== null)
    return pollBars(props.poll);

  return props.prediction === null
    ? []
    : predictionBars(props.prediction);
});
const kindLabel = computed(() =>
  props.poll !== null
    ? t('dashboard.polls.kind.poll')
    : t('dashboard.polls.kind.prediction'),
);
const predictionOpen = computed(() => props.prediction?.status.toUpperCase() === 'ACTIVE');
const closesAt = computed<number | null>(() => {
  if (props.poll !== null)
    return new Date(props.poll.startedAt).getTime() + Number(props.poll.duration) * MS;

  if (props.prediction === null || !predictionOpen.value)
    return null;

  const window = Number(props.prediction.predictionWindow) * MS;

  return new Date(props.prediction.createdAt).getTime() + window;
});

function detail(bar: LeadBar): string {
  const parts = [
    t('dashboard.polls.votes', { count: n(bar.votes) }, bar.votes),
    `${n(bar.percent)}%`,
  ];

  if (props.prediction !== null) {
    parts.push(t('dashboard.polls.points', { count: n(bar.points) }, bar.points));
  }

  return parts.join(' · ');
}

function endCurrentPoll(): void {
  if (props.poll !== null) {
    endPoll.mutate(props.poll.id);
  }
}

function lockCurrentPrediction(): void {
  if (props.prediction !== null) {
    lockPrediction.mutate(props.prediction.id);
  }
}
</script>

<template>
  <PanelCard tier="lead" :title="t('dashboard.polls.lead.title')" pad>
    <div v-if="running" data-lead="running" class="flex flex-col gap-4">
      <div class="flex flex-wrap items-start justify-between gap-3">
        <div class="flex min-w-0 flex-col gap-1">
          <span class="text-xs text-muted-foreground">{{ kindLabel }}</span>
          <span class="text-lg font-semibold">{{ title }}</span>
        </div>
        <span v-if="closesAt !== null" class="text-sm font-medium tabular-nums">
          <Countdown :target="closesAt" :label="title" />
        </span>
        <span v-else class="text-sm text-muted-foreground">{{ t('dashboard.polls.lead.locked') }}</span>
      </div>
      <ul class="flex flex-col gap-3">
        <li v-for="bar in bars" :key="bar.id" class="flex flex-col gap-1.5">
          <div class="flex items-baseline justify-between gap-3 text-sm">
            <span :class="bar.leading ? 'font-medium' : 'text-muted-foreground'">{{ bar.title }}</span>
            <span class="text-muted-foreground tabular-nums">{{ detail(bar) }}</span>
          </div>
          <PollBar size="lg" :tone="bar.leading ? 'accent' : 'muted'" :percent="bar.percent" />
        </li>
      </ul>
      <div v-if="canChange" class="flex flex-wrap gap-2">
        <Button
          v-if="poll !== null"
          data-lead-end
          :disabled="endPoll.isPending.value"
          @click="endCurrentPoll"
        >
          {{ t('dashboard.polls.lead.endPoll') }}
        </Button>
        <Button
          v-else-if="predictionOpen"
          data-lead-lock
          :disabled="lockPrediction.isPending.value"
          @click="lockCurrentPrediction"
        >
          {{ t('dashboard.polls.lead.lock') }}
        </Button>
        <Button v-else data-lead-resolve @click="emit('openPrediction')">
          {{ t('dashboard.polls.lead.resolve') }}
        </Button>
        <Button v-if="prediction !== null && predictionOpen" variant="ghost" @click="emit('openPrediction')">
          {{ t('dashboard.polls.lead.details') }}
        </Button>
      </div>
    </div>
    <div v-else data-lead="empty" class="flex flex-col gap-3">
      <p class="text-sm text-muted-foreground">{{ t('dashboard.polls.lead.idle') }}</p>
      <div class="flex flex-wrap gap-2">
        <Button
          v-if="can('live-ops:polls:write')"
          data-lead-start="poll"
          variant="default"
          @click="emit('startPoll')"
        >
          {{ t('dashboard.polls.lead.startPoll') }}
        </Button>
        <Button
          v-if="can('live-ops:predictions:write')"
          data-lead-start="prediction"
          :variant="can('live-ops:polls:write') ? 'outline' : 'default'"
          @click="emit('startPrediction')"
        >
          {{ t('dashboard.polls.lead.startPrediction') }}
        </Button>
      </div>
    </div>
  </PanelCard>
</template>
