<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The scroll region of a page. The frame's <main> never scrolls, so a page keeps its PageHeader fixed on top
// and puts the rest in this body; the body scrolls when the content is taller than the window.
// dense: a list page whose lead panel fills the height and scrolls inside itself, so the body does not scroll.
defineProps<{ dense?: boolean }>();
</script>

<template>
  <div
    v-if="dense"
    data-slot="page-body"
    class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4"
  >
    <slot />
  </div>
  <div
    v-else
    data-slot="page-body"
    class="flex min-h-0 min-w-0 flex-1 flex-col gap-4 overflow-y-auto p-6 pr-[calc(1.5rem-var(--scrollbar-width))] scrollbar-gutter-stable max-md:p-4 max-md:pr-[calc(1rem-var(--scrollbar-width))] *:shrink-0"
  >
    <slot />
  </div>
</template>
