<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// How many characters a field holds against its limit, shown at the end of the label row.
import type { HTMLAttributes } from 'vue';
import { computed } from 'vue';

import { cn } from '@/lib/utils';

const props = defineProps<{ value: string; max: number; class?: HTMLAttributes['class'] }>();

const full = computed(() => props.value.length >= props.max);
</script>

<template>
  <span
    data-slot="field-count"
    aria-live="polite"
    :class="cn('ml-auto text-sm tabular-nums', full ? 'text-destructive' : 'text-muted-foreground', props.class)"
  >{{ value.length }}/{{ max }}</span>
</template>
