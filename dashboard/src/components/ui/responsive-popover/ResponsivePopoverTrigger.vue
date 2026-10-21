<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
