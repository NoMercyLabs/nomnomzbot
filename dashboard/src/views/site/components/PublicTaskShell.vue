<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The frame of one compact public task (sign in, accept an invite, a page that is not there): the language
// picker, the brand as a link home, and one card column. The page's task title is the page's only h1, so the
// brand is a link, not a heading. The `below` slot holds the small print under the card.
import AppMark from '@/components/layout/AppMark.vue';
import LanguagePicker from '@/components/layout/LanguagePicker.vue';

defineProps<{ brand: string }>();
</script>

<template>
  <main class="relative flex min-h-svh flex-col items-center justify-center gap-6 p-6">
    <div class="absolute top-4 right-4"><LanguagePicker /></div>
    <RouterLink
      to="/"
      data-slot="task-brand"
      class="flex items-center gap-2 rounded-md text-base font-semibold"
    >
      <AppMark />
      {{ brand }}
    </RouterLink>
    <div data-slot="task-column" class="flex w-full max-w-100 flex-col items-center gap-6">
      <slot />
      <div v-if="$slots.below" data-slot="task-below" class="w-full">
        <slot name="below" />
      </div>
    </div>
  </main>
</template>
