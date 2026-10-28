<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One option of the Readability view: title and help on the left, the control on the right.
import RowText from '@/components/layout/RowText.vue';

defineProps<{
  title: string;
  description?: string;
  hint?: string;
}>();
</script>

<template>
  <div class="flex min-h-11 flex-wrap items-center gap-x-4 gap-y-1.5 px-3 py-2">
    <div class="min-w-0 flex-1 basis-56">
      <RowText :title="title" :description="description" :hint="hint" />
    </div>
    <div class="flex items-center gap-3"><slot /></div>
  </div>
</template>
