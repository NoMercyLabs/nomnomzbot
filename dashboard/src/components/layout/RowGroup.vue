<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The V22 grouped list: items that belong together sit on the muted surface, a 1px divider
// (foreground at 10%) between them. Radius 12 with 4px padding, so a row inside is 12 - 4 = 8.
</script>

<template>
  <div
    class="rounded-[12px] border border-border bg-muted p-1 [&>*+*]:border-t [&>*+*]:border-foreground/10"
  >
    <slot />
  </div>
</template>
