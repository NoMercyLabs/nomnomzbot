<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One cap that applies to a single message: its name and its value. There is nothing to use up over time, so
// there is no bar.
defineProps<{
  label: string;
  value: string;
}>();
</script>

<template>
  <div data-limit-row class="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 px-3 py-2.5">
    <span class="min-w-0 flex-1 basis-48 text-sm font-medium">{{ label }}</span>
    <span class="text-sm tabular-nums text-muted-foreground">{{ value }}</span>
  </div>
</template>
