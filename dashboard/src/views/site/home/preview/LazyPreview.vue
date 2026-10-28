<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The only way a page reaches a preview. The preview code (the real pages, the router, the sample answers) is a
// chunk of its own: it is fetched when the spot comes near the screen, so the first load of the home page stays
// small. The hero asks for it at once (eager) but still after the page paints. A skeleton holds the place.
import { useIntersectionObserver } from '@vueuse/core';
import { defineAsyncComponent, ref, useTemplateRef } from 'vue';

import { Skeleton } from '@/components/ui/skeleton';

const PreviewFrame = defineAsyncComponent(() => import('./PreviewFrame.vue'));

const props = withDefaults(
  defineProps<{
    path: string;
    label: string;
    shell?: boolean;
    height?: number;
    eager?: boolean;
  }>(),
  {
    shell: false,
    height: 640,
    eager: false,
  },
);

// How far below the screen the spot may be before its preview is fetched.
const nearMargin = '600px';

const spot = useTemplateRef<HTMLElement>('spot');
const near = ref(props.eager);

const { stop } = useIntersectionObserver(
  spot,
  ([entry]) => {
    if (!entry?.isIntersecting) return;

    near.value = true;
    stop();
  },
  { rootMargin: nearMargin },
);
</script>

<template>
  <div ref="spot" class="w-full" data-testid="lazy-preview">
    <PreviewFrame
      v-if="near"
      :path="path"
      :label="label"
      :shell="shell"
      :height="height"
    />
    <Skeleton v-else class="aspect-video w-full rounded-md" :aria-label="label" />
  </div>
</template>
