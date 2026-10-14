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

import { Sheet, SheetContent, SheetDescription, SheetTitle } from '@/components/ui/sheet';

// Data hooks and attributes from the page land on the panel itself, beside the content or in the drawer.
defineOptions({ inheritAttrs: false });

withDefaults(defineProps<{ label: string; side?: 'left' | 'right' }>(), { side: 'right' });

const open = defineModel<boolean>('open', { required: true });
const roomy = useMediaQuery('(min-width: 1280px)');

// The drawer opens without picking a control: focus on its first button (often Reset) would arm Enter.
function keepFocusOff(event: Event): void {
  event.preventDefault();
}
</script>

<template>
  <aside
    v-if="roomy && open"
    v-bind="$attrs"
    data-slot="side-panel"
    class="flex w-80 min-h-0 flex-none flex-col *:flex-1"
  >
    <slot />
  </aside>
  <Sheet v-else-if="!roomy" v-model:open="open">
    <SheetContent
      v-bind="$attrs"
      data-slot="side-panel"
      data-drawer
      :side="side"
      :show-close-button="false"
      @open-auto-focus="keepFocusOff"
      class="w-88 max-w-[90vw] gap-0 p-0 sm:max-w-none *:flex-1 *:rounded-none *:border-0"
    >
      <SheetTitle class="sr-only">{{ label }}</SheetTitle>
      <SheetDescription class="sr-only">{{ label }}</SheetDescription>
      <slot />
    </SheetContent>
  </Sheet>
</template>
