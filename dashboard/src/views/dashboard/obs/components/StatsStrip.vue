<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// OBS load at a glance: CPU, FPS and skipped frames. Bitrate is not in OBS's stats read, so it is not shown.
import { computed } from 'vue';

import type { ObsStats } from '@/api/obsOutputs';

import { skippedPercent } from '../outputFormat';

const props = defineProps<{ stats: ObsStats | null | undefined }>();

const cells = computed<{ key: string; value: string }[]>(() => {
  const stats = props.stats;

  if (!stats)
    return [];

  return [
    { key: 'cpu', value: `${Number(stats.cpuUsage).toFixed(1)}%` },
    { key: 'fps', value: Number(stats.activeFps).toFixed(1) },
    {
      key: 'render',
      value: `${stats.renderSkippedFrames} (${skippedPercent(stats.renderSkippedFrames, stats.renderTotalFrames)}%)`,
    },
    {
      key: 'dropped',
      value: `${stats.outputSkippedFrames} (${skippedPercent(stats.outputSkippedFrames, stats.outputTotalFrames)}%)`,
    },
  ];
});
</script>

<template>
  <dl v-if="stats" data-obs-stats class="grid grid-cols-2 gap-x-4 gap-y-2 px-4 py-3 sm:grid-cols-4">
    <div v-for="cell in cells" :key="cell.key" :data-stat="cell.key">
      <dt class="text-xs text-muted-foreground">{{ $t(`dashboard.obs.stats.${cell.key}`) }}</dt>
      <dd class="text-sm font-medium tabular-nums">{{ cell.value }}</dd>
    </div>
  </dl>
</template>
