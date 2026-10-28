<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One group of the queue: a quiet heading with the item count, then its rows.
defineProps<{
  kind: string;
  title: string;
  count: number;
}>();
</script>

<template>
  <section :data-queue-section="kind" class="flex flex-col gap-1 not-first:mt-3">
    <h3 class="flex items-center gap-2 px-3 pt-1 text-sm font-medium text-muted-foreground">
      {{ title }}
      <span class="text-xs tabular-nums">{{ count }}</span>
    </h3>
    <slot />
  </section>
</template>
