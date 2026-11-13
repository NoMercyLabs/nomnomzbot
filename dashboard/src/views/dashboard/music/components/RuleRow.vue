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
// The row is a SummaryRow. The label around it (display: contents, no box of its own) keeps what a label gave:
// a press on the words reaches the control, and the control is named by the words.
import SummaryRow from '@/components/layout/SummaryRow.vue';

defineProps<{
  title: string;
  description?: string;
}>();
</script>

<template>
  <label data-slot="rule-row" class="contents">
    <SummaryRow :title="title" :detail="description">
      <template #end>
        <slot />
      </template>
    </SummaryRow>
  </label>
</template>
