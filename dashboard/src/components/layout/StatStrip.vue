<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A row of StatTile that wraps by width, so four tiles stay four on a wide window and stack on a phone.
// On a phone exactly three tiles stack in one column (a two-column grid would leave one alone).
// Any other odd count lets the last tile span both columns, so none sits alone at half width.
</script>

<template>
  <div
    data-slot="stat-strip"
    class="grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-4 max-sm:[&:has(>:last-child:nth-child(3))]:grid-cols-1 max-sm:[&>:last-child:nth-child(odd):not(:nth-child(3))]:col-span-2"
  >
    <slot />
  </div>
</template>
