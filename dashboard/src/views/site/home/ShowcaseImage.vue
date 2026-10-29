<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One still picture of a dashboard page, 1920x1080, from public/showcase. A plain image the browser can cache and
// decode off the main thread. The hero's picture loads at once; every picture below it loads when it nears the screen.
import { computed } from 'vue';

import { showcaseImage, type PreviewArea, type ShowcaseKind } from './preview/previewAreas';

const props = withDefaults(
  defineProps<{
    area: PreviewArea;
    kind: ShowcaseKind;
    label: string;
    eager?: boolean;
  }>(),
  { eager: false },
);

const source = computed(() => showcaseImage(props.area, props.kind));
</script>

<template>
  <img
    :src="source"
    :alt="label"
    width="1920"
    height="1080"
    :loading="eager ? 'eager' : 'lazy'"
    :fetchpriority="eager ? 'high' : 'auto'"
    decoding="async"
    draggable="false"
    class="aspect-video h-auto w-full select-none rounded-md border border-border bg-background"
    data-testid="showcase-image"
  />
</template>
