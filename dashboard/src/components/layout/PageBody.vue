<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The scroll region of a page. The frame's <main> never scrolls, so a page keeps its PageHeader fixed on top
// and puts the rest in this body; the body scrolls when the content is taller than the window.
</script>

<template>
  <div data-slot="page-body" class="flex min-h-0 min-w-0 flex-1 flex-col gap-4 overflow-y-auto p-6 max-md:p-4 *:shrink-0">
    <slot />
  </div>
</template>
