<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One rule inside a RowGroup: its title and help on the left, its control (the slot) on the right.
// The whole row is a label, so a press on the words reaches the control. Radius 8 fits a RowGroup (12 - 4).
import { useId } from 'vue';

import RowText from '@/components/layout/RowText.vue';

defineProps<{
  title: string;
  description?: string;
}>();

const titleId = useId();
</script>

<template>
  <label
    data-slot="rule-row"
    class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2 hover:bg-foreground/5"
  >
    <span class="min-w-0 flex-1">
      <RowText :title="title" :title-id="titleId" :description="description" />
    </span>
    <slot />
  </label>
</template>
