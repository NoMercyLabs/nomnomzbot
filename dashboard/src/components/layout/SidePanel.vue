<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A panel beside the page content. Where the content keeps its room (laptop width and up) it sits in the row;
// narrower, it slides over the page as a drawer from its side, so the content never gets squeezed.
// Stoney 2026-10-14: "Make sure you overlay what no longer fits with a drawer from left or right".
import { useMediaQuery } from '@vueuse/core';
import { computed } from 'vue';

import { Sheet, SheetContent, SheetDescription, SheetTitle } from '@/components/ui/sheet';
import { provideDrawerClose } from '@/composables/useDrawerClose';
import { useFrameRail } from '@/composables/useFrameRail';

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
const wide = useMediaQuery('(min-width: 1280px)');
// The rail is 200px narrower than the sidebar.
const fitsBesideRail = useMediaQuery('(min-width: 1080px)');
const folds = computed(() =>
  props.seeThrough && !wide.value && fitsBesideRail.value);
const roomy = computed(() =>
  wide.value || folds.value);

useFrameRail(computed(() =>
  folds.value && open.value));

// As a drawer it covers the button that opened it, so its top panel carries a close button.
provideDrawerClose(() => {
  open.value = false;
}, computed(() =>
  !roomy.value));

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
