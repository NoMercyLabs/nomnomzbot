<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One kept recap drawn by the real raid_recap widget (the same file the overlay serves), not a copy. The widget
// talks to the overlay host's global `NomNomz`; this page is that host for one recap. The widget draws on a fixed
// frame of the stream's output size, and the frame is scaled down to fit the dialog; the export reads the unscaled frame.
import { useElementSize } from '@vueuse/core';
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';

import type { StreamRecap } from '@/api/streamRecaps';

import RaidRecap from '../../../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/raid_recap.vue';
import type { RecapSize } from './recapExport';

const props = defineProps<{ recap: StreamRecap; size: RecapSize }>();

type Handler = (data: { stream: { id: string } }) => void;

// The part of the overlay host's `NomNomz` this widget uses.
type Host = {
  onSettings: (callback: (settings: { holdSeconds: number }) => void) => void;
  on: (type: string, callback: Handler) => void;
  off: (type: string) => void;
  data: { streamStats: () => Promise<StreamRecap['stats']> };
};

const handlers: Record<string, Handler> = {};
const scope = globalThis as { NomNomz?: Host };
const previous = scope.NomNomz;
// The widget hides itself after its hold; a saved recap stays up for as long as the page is open.
const HOLD_SECONDS = 86_400;

scope.NomNomz = {
  onSettings: (callback) => callback({ holdSeconds: HOLD_SECONDS }),
  on: (type, callback) => {
    handlers[type] = callback;
  },
  off: (type) => {
    delete handlers[type];
  },
  data: { streamStats: async () => props.recap.stats },
};

const frame = ref<HTMLElement | null>(null);
const box = ref<HTMLElement | null>(null);
const { width: boxWidth } = useElementSize(box);
const scale = computed(() =>
  boxWidth.value > 0
    ? boxWidth.value / props.size.width
    : 1);

onMounted(() => handlers['raid_recap.show']?.({ stream: { id: props.recap.streamId } }));

onBeforeUnmount(() => {
  scope.NomNomz = previous;
});

defineExpose({ frame });
</script>

<template>
  <div ref="box" class="w-full overflow-hidden rounded-md" :style="{ aspectRatio: `${size.width} / ${size.height}` }">
    <div
      ref="frame"
      data-recap-frame
      class="relative origin-top-left overflow-hidden bg-black"
      :style="{ width: `${size.width}px`, height: `${size.height}px`, transform: `scale(${scale})`, contain: 'layout paint' }"
    >
      <RaidRecap />
    </div>
  </div>
</template>
