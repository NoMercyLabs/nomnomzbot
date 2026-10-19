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
import { computed, ref, watch } from 'vue';

import Cross from '~icons/nnz/cross';

import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useDrawerClose } from '@/composables/useDrawerClose';
import type { PanelState } from '@/composables/usePanelState';

const props = withDefaults(
  defineProps<{
    title: string;
    description?: string;
    tier?: 'lead' | 'default' | 'quiet';
    pad?: boolean;
    // Inside a sheet or dialog the surface already frames it, so the card drops its own border.
    flush?: boolean;
    // One section in a flat list (a profile tab): a small heading over the content, no box, padding or dividers.
    bare?: boolean;
    // No head at all: the panel is only its body (a docked chat that needs every pixel for the feed).
    headless?: boolean;
    // The body never scrolls: its content (a virtual list) owns the one scroller, so there is no scroll in a scroll.
    still?: boolean;
    state?: PanelState;
    emptyText?: string;
    errorText?: string;
  }>(),
  {
    description: undefined,
    tier: 'default',
    pad: false,
    flush: false,
    bare: false,
    headless: false,
    still: false,
    state: 'ready',
    emptyText: undefined,
    errorText: undefined,
  },
);

defineEmits<{ retry: [] }>();

const drawerClose = useDrawerClose();

const rootClass = computed<string | string[]>(() => {
  if (props.bare)
    return 'gap-3';

  return ['overflow-hidden', props.flush
    ? 'h-full'
    : 'rounded-lg border border-border bg-card'];
});

const bodyPad = computed<string>(() => {
  if (props.headless)
    return '';

  return props.pad
    ? 'p-4'
    : 'p-2';
});

const bodyClass = computed<string | string[]>(() => {
  if (props.bare)
    return '';

  return [props.still
    ? 'overflow-hidden'
    : 'overflow-y-auto', bodyPad.value];
});

const footerClass = computed<string | string[]>(() => {
  if (props.bare)
    return '';

  return ['border-t border-border', props.headless
    ? 'p-2'
    : 'px-4 py-3'];
});

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
    class="flex min-h-0 min-w-0 flex-col text-card-foreground"
    :class="rootClass"
  >
    <!-- The title keeps its whole width; actions sit beside it when they fit, else on their own row.
         The description runs the full width below. -->
    <header
      v-if="!headless"
      class="flex flex-none flex-wrap content-center items-center gap-x-2 gap-y-1"
      :class="bare ? '' : 'min-h-13 border-b border-border px-4 py-2'"
    >
      <h2
        class="max-w-full flex-[1_0_auto]"
        :class="[
          bare ? 'text-sm' : 'text-base',
          tier === 'quiet' ? 'font-medium text-muted-foreground' : 'font-semibold',
        ]"
      >
        {{ title }}
      </h2>
      <div
        v-if="$slots.actions || drawerClose.shown.value"
        data-slot="panel-actions"
        class="flex max-w-full min-w-0 items-center gap-2"
      >
        <slot name="actions" />
        <Button
          v-if="drawerClose.shown.value"
          data-drawer-close
          variant="ghost"
          size="icon"
          :aria-label="$t('common.close')"
          @click="drawerClose.close"
        >
          <Cross class="size-5" aria-hidden="true" />
        </Button>
      </div>
      <p v-if="description" class="basis-full text-sm text-muted-foreground">{{ description }}</p>
    </header>
    <div class="min-h-0 flex-1" :class="bodyClass">
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
        class="text-sm text-muted-foreground"
        :class="bare ? '' : 'flex h-full flex-col items-center justify-center gap-1 p-4 text-center'"
      >
        {{ emptyText ?? $t('common.panel.empty') }}
      </div>
      <div
        v-else
        data-slot="panel-error"
        role="alert"
        class="flex gap-3 text-sm"
        :class="bare ? 'items-center' : 'h-full flex-col items-center justify-center p-4 text-center'"
      >
        <span class="text-destructive">{{ errorText ?? $t('common.panel.error') }}</span>
        <Button variant="outline" size="sm" @click="$emit('retry')">{{
          $t('common.panel.retry')
        }}</Button>
      </div>
    </div>
    <footer
      v-if="$slots.footer"
      class="flex flex-none flex-wrap items-center gap-2"
      :class="footerClass"
    >
      <slot name="footer" />
    </footer>
  </section>
</template>
