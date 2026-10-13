<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Time left until a target moment: m:ss, or h:mm:ss from an hour on. Emits zero once per target.
// Screen readers do not hear the seconds: the visible clock is hidden from them, and a quiet
// line with the minutes left changes only when the minute changes (the box is not a live region).
import { useIntervalFn, useNow } from '@vueuse/core'
import { computed, watch, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import { formatCountdown } from './formatCountdown'

// The file is named Countdown (spec: frontend-vue-components.md:69); the rule needs a two-word component name.
defineOptions({ name: 'CountdownTimer' })

const props = defineProps<{ target: Date | number; label: string }>()
const emit = defineEmits<{ zero: [] }>()

const { t } = useI18n()
const { now, resume } = useNow({
  controls: true,
  scheduler: (tick: () => void) => useIntervalFn(tick, 1000),
})

const targetMs = computed<number>(() => new Date(props.target).getTime())
const remaining = computed<number>(() =>
  Math.max(0, Math.ceil((targetMs.value - now.value.getTime()) / 1000)),
)
const minutesLeft = computed<number>(() => Math.ceil(remaining.value / 60))
const spoken: ComputedRef<string> = computed<string>(() =>
  remaining.value === 0
    ? t('common.countdown.now', { label: props.label })
    : t('common.countdown.minutes', { label: props.label, count: minutesLeft.value }),
)

// A new target is measured from the real clock at once, not from the last tick.
watch(
  targetMs,
  () => {
    now.value = new Date()
    resume()
  },
  { flush: 'sync' },
)

let firedFor: number | null = null

watch(
  [targetMs, remaining],
  () => {
    if (remaining.value !== 0 || firedFor === targetMs.value) return

    firedFor = targetMs.value
    emit('zero')
  },
  { immediate: true },
)
</script>

<template>
  <span
    data-slot="countdown"
    :data-soon="remaining > 0 && remaining < 60 ? 'true' : undefined"
    role="timer"
    class="inline-block"
  >
    <span
      data-slot="countdown-time"
      aria-hidden="true"
      class="tabular-nums"
      :class="remaining > 0 && remaining < 60 ? 'text-destructive' : ''"
      >{{ formatCountdown(remaining) }}</span
    >
    <span class="sr-only">{{ spoken }}</span>
  </span>
</template>
