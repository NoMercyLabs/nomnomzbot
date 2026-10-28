<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Status is a dot plus words, never a tinted pill (C5). The words carry the meaning;
// the dot has the three mockup colors only (frame.css:148-150).
export type StatusTone = 'ok' | 'bad' | 'live' | 'off';

defineProps<{ tone: StatusTone; label: string }>();

const dotClass = {
  ok: 'bg-success',
  bad: 'bg-destructive',
  live: 'bg-destructive',
  off: 'bg-muted-foreground',
} as const satisfies Record<StatusTone, string>;
</script>

<template>
  <span
    data-slot="status-badge"
    :data-tone="tone"
    class="inline-flex items-center gap-2 text-sm whitespace-nowrap"
  >
    <span aria-hidden="true" class="size-2 flex-none rounded-full" :class="dotClass[tone]" />
    {{ label }}
  </span>
</template>
