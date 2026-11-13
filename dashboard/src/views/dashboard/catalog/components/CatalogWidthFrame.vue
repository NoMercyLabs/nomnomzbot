<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Renders an example in a frame of one chosen width: desktop, narrow or phone. The window never resizes, so
// only the parts that follow their container (container queries, wrapping, truncation) change; the parts that
// follow the screen (the md and tablet breakpoints) stay as they are.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import SegmentedControl from '@/components/layout/SegmentedControl.vue';

defineProps<{ title: string }>();

const { t } = useI18n();

type FrameWidth = 'desktop' | 'narrow' | 'phone';

const FRAME_CLASS = {
  desktop: 'w-full',
  narrow: 'w-full max-w-xl',
  phone: 'w-full max-w-sm',
} as const satisfies Record<FrameWidth, string>;

const width = ref<FrameWidth>('desktop');

const options = computed(() => [
  { value: 'desktop', label: t('dashboard.catalog.width.desktop') },
  { value: 'narrow', label: t('dashboard.catalog.width.narrow') },
  { value: 'phone', label: t('dashboard.catalog.width.phone') },
] as const satisfies readonly { value: FrameWidth; label: string }[]);
</script>

<template>
  <div data-catalog-frame class="grid gap-3">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h3 class="text-sm font-medium">{{ title }}</h3>
      <SegmentedControl v-model="width" :label="t('dashboard.catalog.width.label')" :options="options" />
    </div>
    <!-- The inner box is the container: a rounded 12px border around a 4px gap keeps the example's own corners concentric. -->
    <div
      data-catalog-frame-box
      :data-frame-width="width"
      class="@container min-w-0 overflow-hidden rounded-[12px] border border-dashed border-border p-1"
      :class="FRAME_CLASS[width]"
    >
      <slot />
    </div>
  </div>
</template>
