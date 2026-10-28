<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { inject, ref } from 'vue';

import { DrawerTrigger } from '@/components/ui/drawer';
import { PopoverTrigger } from '@/components/ui/popover';

import { sheetKey } from './context';

const sheet = inject(sheetKey, ref(false));
</script>

<template>
  <DrawerTrigger v-if="sheet" as-child>
    <slot />
  </DrawerTrigger>
  <PopoverTrigger v-else as-child>
    <slot />
  </PopoverTrigger>
</template>
