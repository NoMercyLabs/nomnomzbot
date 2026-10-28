<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import type { PopoverContentProps } from 'reka-ui';
import type { HTMLAttributes } from 'vue';
import { inject, ref } from 'vue';

import { DrawerContent, DrawerDescription, DrawerTitle } from '@/components/ui/drawer';
import { PopoverContent } from '@/components/ui/popover';

import { sheetKey } from './context';

// title: what a screen reader announces for the sheet; the popover has no title of its own.
// class: the popover's own look; the sheet is always full width and takes its body from the slot.
const { title, align, class: popoverClass } = defineProps<{
  title: string;
  align?: PopoverContentProps['align'];
  class?: HTMLAttributes['class'];
}>();

const sheet = inject(sheetKey, ref(false));
</script>

<template>
  <DrawerContent v-if="sheet" data-slot="responsive-sheet">
    <DrawerTitle class="sr-only">{{ title }}</DrawerTitle>
    <DrawerDescription class="sr-only">{{ title }}</DrawerDescription>
    <div class="flex min-h-0 flex-col overflow-y-auto pb-4">
      <slot />
    </div>
  </DrawerContent>
  <PopoverContent v-else :align="align" :class="popoverClass">
    <slot />
  </PopoverContent>
</template>
