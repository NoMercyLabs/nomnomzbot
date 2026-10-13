<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The mockup panel (V13, V21): a card with a head, a body and an optional foot, split by 1px dividers.
// tier: lead = the one panel the page is for; quiet = status the eye may skip, so its title is muted.
// state (C7): the panel keeps its size in every state. A skeleton shows only after 150 ms,
// so a fast answer never flashes one. The default slot shows only in the ready state.
import { useTimeoutFn } from '@vueuse/core';
import { ref, watch } from 'vue';

import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';

export type PanelState = 'ready' | 'loading' | 'empty' | 'error';

const props = withDefaults(
  defineProps<{
    title: string;
    description?: string;
    tier?: 'lead' | 'default' | 'quiet';
    pad?: boolean;
    state?: PanelState;
    emptyText?: string;
    errorText?: string;
  }>(),
  {
    description: undefined,
    tier: 'default',
    pad: false,
    state: 'ready',
    emptyText: undefined,
    errorText: undefined,
  },
);

defineEmits<{ retry: [] }>();

const skeletonShown = ref(false);
const { start, stop } = useTimeoutFn(
  () => {
    skeletonShown.value = true;
  },
  150,
  { immediate: false },
);

watch(
  () => props.state,
  (state: PanelState) => {
    skeletonShown.value = false;

    if (state === 'loading') {
      start();
    } else {
      stop();
    }
  },
  { immediate: true },
);
</script>

<template>
  <section
    data-slot="panel-card"
    :data-state="state"
    class="flex min-h-0 min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-card text-card-foreground"
  >
    <header class="flex min-h-13 flex-none items-center gap-2 border-b border-border px-4 py-2">
      <div class="min-w-0 flex-1">
        <h2
          class="text-base"
          :class="tier === 'quiet' ? 'font-medium text-muted-foreground' : 'font-semibold'"
        >
          {{ title }}
        </h2>
        <p v-if="description" class="text-sm text-muted-foreground">{{ description }}</p>
      </div>
      <slot name="actions" />
    </header>
    <div class="min-h-0 flex-1 overflow-y-auto" :class="pad ? 'p-4' : 'p-2'">
      <slot v-if="state === 'ready'" />
      <div
        v-else-if="state === 'loading'"
        data-slot="panel-loading"
        class="grid gap-2"
        :aria-busy="true"
      >
        <template v-if="skeletonShown">
          <Skeleton v-for="line in 3" :key="line" class="h-11 rounded-[12px]" />
        </template>
      </div>
      <div
        v-else-if="state === 'empty'"
        data-slot="panel-empty"
        class="flex h-full flex-col items-center justify-center gap-1 p-4 text-center text-sm text-muted-foreground"
      >
        {{ emptyText ?? $t('common.panel.empty') }}
      </div>
      <div
        v-else
        data-slot="panel-error"
        role="alert"
        class="flex h-full flex-col items-center justify-center gap-3 p-4 text-center text-sm"
      >
        <span class="text-destructive">{{ errorText ?? $t('common.panel.error') }}</span>
        <Button variant="outline" size="sm" @click="$emit('retry')">{{
          $t('common.panel.retry')
        }}</Button>
      </div>
    </div>
    <footer
      v-if="$slots.footer"
      class="flex flex-none items-center gap-2 border-t border-border px-4 py-3"
    >
      <slot name="footer" />
    </footer>
  </section>
</template>
