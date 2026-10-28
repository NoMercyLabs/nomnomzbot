<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One OBS output as one dense row: its name, its state, its timer, and the actions at the end.
// The row is as tall as a button; nothing in it wraps, so a panel of four rows stays a short strip.
defineProps<{
  name: string;
  state: string;
  // Lit while the output runs: the state text takes the foreground color, otherwise it is muted.
  on: boolean;
  timer?: string;
}>();
</script>

<template>
  <div data-output-row class="flex min-h-12 items-center gap-3 px-3 py-1.5">
    <span class="w-32 shrink-0 truncate text-sm font-medium">{{ name }}</span>
    <span
      data-output-state
      class="min-w-0 flex-1 truncate text-sm"
      :class="on ? 'text-foreground' : 'text-muted-foreground'"
    >
      {{ state }}
    </span>
    <span
      v-if="timer"
      data-output-timer
      class="shrink-0 text-sm tabular-nums text-muted-foreground"
    >
      {{ timer }}
    </span>
    <div class="flex shrink-0 items-center gap-2">
      <slot />
    </div>
  </div>
</template>
