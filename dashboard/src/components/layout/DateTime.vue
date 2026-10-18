<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
