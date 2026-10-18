<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One dense row with what OBS's own status bar shows, at the top of the page: stream and recording state with
// their timers, bitrate, dropped frames, CPU and FPS; render-skipped, memory and congestion follow when OBS reports them.
// The bitrate is the byte counter's change between two reads, so it shows from the second read on.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ObsStats } from '@/api/obsOutputs';

import {
  byteSample,
  obsStatusFacts,
  withLiveOutputs,
  type ByteSample,
  type LiveOutputs,
  type ObsStatusFacts,
} from '../obsStatusFacts';

// `live` is the newest stream and recording state (a push lands before the next poll); the two timers count by themselves.
const props = defineProps<{
  stats: ObsStats | null | undefined;
  readAt: number;
  live: LiveOutputs | null;
  streamTimer: string;
  recordTimer: string;
}>();

const { n, t } = useI18n();

const polled = ref<ObsStatusFacts | null>(null);
const facts = computed<ObsStatusFacts | null>(() =>
  polled.value && withLiveOutputs(polled.value, props.live));
let previous: ByteSample | null = null;

watch(
  () => props.readAt,
  (at) => {
    const stats = props.stats;

    if (!stats) {
      polled.value = null;

      return;
    }

    polled.value = obsStatusFacts(stats, previous, at);
    previous = byteSample(stats, at);
  },
  { immediate: true },
);

// A figure OBS did not measure has no number to format; its cell shows a dash.
const none = '-';

function percent(fraction: number | null): string {
  if (fraction === null)
    return none;

  return n(fraction, {
    style: 'percent',
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  });
}

function one(value: number | null): string {
  if (value === null)
    return none;

  return n(value, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
}

function megabytes(memory: number | null): string {
  if (memory === null)
    return none;

  return t('dashboard.obs.stats.megabytes', { value: n(Math.round(memory)) });
}

type Cell = {
  key: string;
  value: string;
  on?: boolean;
};

function cells(current: ObsStatusFacts): Cell[] {
  const bitrate = current.streamKbps === null
    ? '-'
    : t('dashboard.obs.stats.kbps', { value: n(current.streamKbps) });

  return [
    {
      key: 'stream',
      value: [t(`dashboard.obs.stats.${current.stream.state}`), props.streamTimer].join(' ').trim(),
      on: current.stream.state !== 'off',
    },
    {
      key: 'record',
      value: [
        t(`dashboard.obs.stats.${current.record.state}`),
        props.recordTimer,
      ].join(' ').trim(),
      on: current.record.state === 'recording',
    },
    { key: 'bitrate', value: bitrate },
    {
      key: 'dropped',
      value: t('dashboard.obs.stats.droppedValue', {
        count: n(current.dropped.skipped),
        share: percent(current.dropped.fraction),
      }),
    },
    {
      key: 'render',
      value: t('dashboard.obs.stats.droppedValue', {
        count: n(current.render.skipped),
        share: percent(current.render.fraction),
      }),
    },
    {
      key: 'cpu',
      value: current.cpu === null
        ? none
        : `${one(current.cpu)}%`,
    },
    {
      key: 'fps',
      value: t('dashboard.obs.stats.fpsValue', {
        current: one(current.fps),
        target: one(current.targetFps),
      }),
    },
    { key: 'memory', value: megabytes(current.memory) },
    { key: 'congestion', value: percent(current.congestion) },
  ];
}
</script>

<template>
  <dl
    v-if="facts"
    data-obs-status-bar
    class="flex flex-wrap items-center gap-x-5 gap-y-1 rounded-lg border border-border bg-card px-3 py-2"
  >
    <div
      v-for="cell in cells(facts)"
      :key="cell.key"
      :data-stat="cell.key"
      class="flex items-baseline gap-1.5"
    >
      <dt class="text-xs text-muted-foreground">{{ $t(`dashboard.obs.stats.${cell.key}`) }}</dt>
      <dd class="text-sm font-medium tabular-nums" :class="cell.on ? 'text-foreground' : 'text-muted-foreground'">
        {{ cell.value }}
      </dd>
    </div>
  </dl>
</template>
