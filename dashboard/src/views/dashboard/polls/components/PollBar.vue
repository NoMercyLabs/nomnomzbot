<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One share bar. accent is for the single leading choice of the running poll; strong marks a history
// winner; muted is every other choice. The accent never reaches a history card.
import { computed } from 'vue';

const props = defineProps<{
  percent: number;
  tone: 'accent' | 'strong' | 'muted';
  size: 'lg' | 'sm';
}>();

const fillClass = computed(() => {
  if (props.tone === 'accent')
    return 'bg-primary';

  return props.tone === 'strong'
    ? 'bg-foreground'
    : 'bg-muted-foreground/40';
});
</script>

<template>
  <span
    class="block overflow-hidden rounded-full bg-muted"
    :class="size === 'lg' ? 'h-2.5' : 'h-1.5'"
    aria-hidden="true"
  >
    <span
      data-bar-fill
      :data-leading="tone === 'muted' ? undefined : true"
      class="block h-full rounded-full"
      :class="fillClass"
      :style="{ width: `${props.percent}%` }"
    />
  </span>
</template>
