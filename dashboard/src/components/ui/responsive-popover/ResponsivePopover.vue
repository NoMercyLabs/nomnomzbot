<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
