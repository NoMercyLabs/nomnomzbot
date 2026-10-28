<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Any moment or length on screen, in the person's language: the variant picks the look (date, time, relative,
// duration, ...). A <time> element with the machine value; the exact date and time show on hover, so a relative
// label ("3 minutes ago") never hides when it happened. `live` re-ticks a relative label.
import { computed } from 'vue';

import { useFormattedTime } from '@/composables/useTime';
import { toMoment, type TimeValue, type TimeVariant } from '@/lib/time';

const props = withDefaults(defineProps<{
  value: TimeValue;
  variant?: TimeVariant;
  live?: boolean;
}>(), {
  variant: 'dateTime',
  live: false,
});

// The variant is read once, when the component is created.
const text = useFormattedTime(() => props.value, props.variant, { live: props.live });
const exact = useFormattedTime(() => props.value, 'dateTimeFull');
const dash = '–';

const isLength = computed<boolean>(() => props.variant === 'duration' || props.variant === 'durationClock');
const machine = computed<string | undefined>(() => {
  if (isLength.value)
    return typeof props.value === 'number'
      ? `PT${Math.round(props.value)}S`
      : undefined;

  return toMoment(props.value)?.toISOString();
});
const title = computed<string | undefined>(() => {
  if (isLength.value || props.variant === 'dateTimeFull')
    return undefined;

  return exact.value || undefined;
});
</script>

<template>
  <time v-if="text" :datetime="machine" :title="title">{{ text }}</time>
  <span v-else>{{ dash }}</span>
</template>
