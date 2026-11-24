<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The numbered guide for the OBS WebSocket server. A step ticks only when the real link state proves it, and one
// fix line names what to do next. It has no primary button: "Test again" asks the page to re-run its OBS reads.
import { computed } from 'vue';

import type { ObsConnection } from '@/api/obs';
import { Button } from '@/components/ui/button';
import Check from '~icons/nnz/check';

import { doneSteps, SETUP_STEPS, setupState, type SetupState } from '../obsHealth';

const props = defineProps<{
  connection: Pick<ObsConnection, 'isEnabled' | 'lastError'>;
  unreachable: boolean;
}>();

defineEmits<{ retest: [] }>();

const state = computed<SetupState>(() => setupState(props.connection, props.unreachable));
const done = computed(() => doneSteps(state.value));
</script>

<template>
  <details
    v-if="state !== 'connected'"
    data-guide
    :data-guide-state="state"
    open
    class="rounded-lg border border-border bg-card p-3"
  >
    <summary class="cursor-pointer text-sm font-medium">{{ $t('dashboard.obs.guide.title') }}</summary>
    <ol class="mt-2 grid gap-2">
      <li
        v-for="(step, index) in SETUP_STEPS"
        :key="step"
        :data-guide-step="step"
        :data-done="done.includes(step)"
        class="flex items-start gap-2 text-sm"
      >
        <span
          class="grid size-5 shrink-0 place-items-center rounded-full border text-xs tabular-nums"
          :class="done.includes(step)
            ? 'border-success text-success'
            : 'border-border text-muted-foreground'"
          aria-hidden="true"
        >
          <Check v-if="done.includes(step)" class="size-3" />
          <template v-else>{{ index + 1 }}</template>
        </span>
        <span :class="done.includes(step) && 'text-muted-foreground'">
          {{ $t(`dashboard.obs.guide.steps.${step}`) }}
          <span v-if="done.includes(step)" class="sr-only">{{ $t('dashboard.obs.guide.done') }}</span>
        </span>
      </li>
    </ol>
    <div class="mt-2 flex flex-wrap items-center gap-2 border-t border-border pt-2">
      <p data-guide-fix class="min-w-0 flex-1 text-sm">{{ $t(`dashboard.obs.guide.fix.${state}`) }}</p>
      <Button
        data-guide-retest
        variant="outline"
        size="sm"
        @click="$emit('retest')"
      >
        {{ $t('dashboard.obs.guide.retest') }}
      </Button>
    </div>
  </details>
</template>
