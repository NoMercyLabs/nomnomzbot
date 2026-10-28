<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A popover on a wide window, a bottom sheet below the tablet width: a 288 px box pinned to the screen edge is
// not a phone menu. Stoney 2026-10-21: "what works on desktop does not work for mobile".
import { provide } from 'vue';

import { Drawer } from '@/components/ui/drawer';
import { Popover } from '@/components/ui/popover';
import { useNarrowScreen } from '@/composables/useLayout';

import { sheetKey } from './context';

const open = defineModel<boolean>('open', { default: false });
const sheet = useNarrowScreen();

provide(sheetKey, sheet);
</script>

<template>
  <Drawer v-if="sheet" v-model:open="open">
    <slot />
  </Drawer>
  <Popover v-else v-model:open="open">
    <slot />
  </Popover>
</template>
