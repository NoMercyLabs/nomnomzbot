<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One option of a RowGroup whose control is not a switch: the title, help and hint on the left, and the control or the
// action (an input, a select, a slider, a reset or remove button) on the right. SettingRow owns the switch row and
// SummaryRow the row that names a thing; this row explains an option and acts on it. On a narrow screen the action
// drops under the text instead of squeezing the title.
import RowText from './RowText.vue';

defineProps<{
  title: string;
  description?: string;
  hint?: string;
}>();
</script>

<template>
  <div data-slot="action-row" class="flex min-h-11 flex-wrap items-center gap-x-4 gap-y-1.5 px-3 py-2">
    <div data-slot="action-row-text" class="min-w-0 flex-1 basis-56">
      <RowText :title="title" :description="description" :hint="hint" />
    </div>
    <div data-slot="action-row-action" class="flex items-center gap-3"><slot /></div>
  </div>
</template>
