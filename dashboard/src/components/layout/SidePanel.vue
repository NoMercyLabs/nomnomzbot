<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A panel beside the page content. Where the content keeps its room (laptop width and up) it sits in the row;
// narrower, it slides over the page as a drawer from its side, so the content never gets squeezed.
// Stoney 2026-10-14: "Make sure you overlay what no longer fits with a drawer from left or right".
import { onKeyStroke } from '@vueuse/core';
import { computed } from 'vue';

import { Sheet, SheetContent, SheetDescription, SheetTitle } from '@/components/ui/sheet';
import { provideDrawerClose } from '@/composables/useDrawerClose';
import { useFrameRail } from '@/composables/useFrameRail';
import { useScreenFrom } from '@/composables/useLayout';

// Data hooks and attributes from the page land on the panel itself, beside the content or in the drawer.
defineOptions({ inheritAttrs: false });

// A pinned panel always sits in the row where it fits and fills the column its layout gives it; open then
// drives only the drawer.
// A see-through panel changes what the page shows (Stoney 2026-10-14, the chat settings): it folds the sidebar
// to its rail to stay in the row, and where even that has no room its drawer leaves the page unblurred.
const props = withDefaults(defineProps<{
  label: string;
  side?: 'left' | 'right';
  pinned?: boolean;
  seeThrough?: boolean;
}>(), {
  side: 'right',
  pinned: false,
  seeThrough: false,
});

const open = defineModel<boolean>('open', { required: true });
const wide = useScreenFrom('wide');
const fitsBesideRail = useScreenFrom('besideRail');
const folds = computed(() =>
  props.seeThrough && !wide.value && fitsBesideRail.value);
const roomy = computed(() =>
  wide.value || folds.value);

useFrameRail(computed(() =>
  folds.value && open.value));

// Its top panel carries the one close button, beside the content and in the drawer; a pinned panel beside the
// content never closes.
provideDrawerClose(() => {
  open.value = false;
}, computed(() =>
  !roomy.value || !props.pinned));

// Beside the content the panel has no dialog, so Escape closes it here; the drawer closes itself.
onKeyStroke('Escape', (event: KeyboardEvent) => {
  if (roomy.value && open.value && !props.pinned && !event.defaultPrevented) {
    open.value = false;
  }
});

// The drawer opens without picking a control: focus on its first button (often Reset) would arm Enter.
function keepFocusOff(event: Event): void {
  event.preventDefault();
}
</script>

<template>
  <aside
    v-if="roomy && (open || pinned)"
    data-slot="side-panel"
    class="flex min-h-0 flex-col *:flex-1"
    :class="pinned ? 'min-w-0' : 'w-120 flex-none'"
    v-bind="$attrs"
  >
    <slot />
  </aside>
  <Sheet v-else-if="!roomy" v-model:open="open">
    <SheetContent
      data-slot="side-panel"
      data-drawer
      :data-see-through="seeThrough || undefined"
      :side="side"
      :show-close-button="false"
      @open-auto-focus="keepFocusOff"
      class="gap-0 p-0 data-[side=left]:w-120 data-[side=right]:w-120 data-[side=left]:max-w-[90vw] data-[side=right]:max-w-[90vw] data-[side=left]:sm:max-w-[90vw] data-[side=right]:sm:max-w-[90vw] *:flex-1 *:rounded-none *:border-0"
      v-bind="$attrs"
    >
      <SheetTitle class="sr-only">{{ label }}</SheetTitle>
      <SheetDescription class="sr-only">{{ label }}</SheetDescription>
      <slot />
    </SheetContent>
  </Sheet>
</template>
