<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The text of a setting row: the title with a small hint (the default) at its right end, and the help line below.
// The hint sits on the title line so a long help line never wraps because of it.
defineProps<{
  title: string;
  titleId?: string;
  description?: string;
  hint?: string;
}>();
</script>

<template>
  <span class="block min-w-0">
    <span :id="titleId" data-row-title class="block text-sm font-medium wrap-break-word">{{
      title
    }}</span>
    <span v-if="description" data-row-description class="block text-sm text-muted-foreground">{{
      description
    }}</span>
    <span v-if="hint" data-row-hint class="block text-xs text-muted-foreground">{{ hint }}</span>
  </span>
</template>
