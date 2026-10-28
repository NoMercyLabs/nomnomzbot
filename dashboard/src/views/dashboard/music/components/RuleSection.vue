<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One titled section of the request rules: a 14 medium title, an optional muted help line, then the rows.
// The rows go in the slot; the caller wraps them in a RowGroup (or a RadioGroup rendered as one).
import { useId } from 'vue';

defineProps<{
  title: string;
  description?: string;
}>();

const titleId = useId();
</script>

<template>
  <section data-rules-section class="grid gap-2" :aria-labelledby="titleId">
    <h3 :id="titleId" class="text-sm font-medium">{{ title }}</h3>
    <p v-if="description" class="text-sm text-muted-foreground">{{ description }}</p>
    <slot />
  </section>
</template>
