<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One group of raid rules: a title, one plain line about what the group does, then its rows.
defineProps<{ title: string; hint: string }>();
</script>

<template>
  <section class="flex flex-col gap-3">
    <header class="flex flex-col gap-0.5">
      <h3 class="text-sm font-medium">{{ title }}</h3>
      <p class="text-sm text-muted-foreground">{{ hint }}</p>
    </header>
    <div class="flex flex-col gap-4">
      <slot />
    </div>
  </section>
</template>
