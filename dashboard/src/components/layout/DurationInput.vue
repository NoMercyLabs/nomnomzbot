<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A length of time typed as minutes and seconds ("4:30"). The model stays in seconds, the unit the API takes,
// so nobody does the math. Bare digits are minutes. Text that is not a length sends NaN, which fails any range
// check in the parent; the field also flags itself when the value is NaN or outside min and max (seconds).
// Stoney 2026-10-18: Longest song, in seconds, needs to be minutes: don't make the user do math.
import { computed, ref, watch } from 'vue';

import { Input } from '@/components/ui/input';

import { formatClock, parseClock } from './formatCountdown';

const props = withDefaults(defineProps<{
  min?: number;
  max?: number;
}>(), {
  min: 0,
  max: Number.POSITIVE_INFINITY,
});

const seconds = defineModel<number>({ required: true });
const text = ref(formatClock(seconds.value));

// The field keeps what the person is typing; a value that arrives from outside rewrites it.
watch(seconds, (next: number) => {
  if (parseClock(text.value) !== next) {
    text.value = formatClock(next);
  }
});

function typed(value: string | number): void {
  text.value = String(value);
  seconds.value = parseClock(text.value);
}

const invalid = computed<boolean>(() =>
  !(seconds.value >= props.min && seconds.value <= props.max));
</script>

<template>
  <Input
    :model-value="text"
    type="text"
    inputmode="numeric"
    placeholder="m:ss"
    :aria-invalid="invalid || undefined"
    @update:model-value="typed"
  />
</template>
