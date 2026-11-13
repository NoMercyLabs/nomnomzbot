<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The heading of one marketing section: a title and one line under it. level 2 is a section of the page (24px),
// level 3 is a group inside a section (20px). It draws only the heading, never the section around it.
withDefaults(
  defineProps<{
    title: string;
    description?: string;
    level?: 2 | 3;
  }>(),
  {
    description: undefined,
    level: 2,
  },
);
</script>

<template>
  <div data-slot="section-heading" class="flex flex-col gap-2">
    <component
      :is="`h${level}`"
      class="font-semibold"
      :class="level === 2 ? 'text-2xl' : 'text-xl'"
    >
      {{ title }}
    </component>
    <p v-if="description" class="text-base text-muted-foreground">{{ description }}</p>
  </div>
</template>
