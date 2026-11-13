<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The action stack of a compact public task card: full-width buttons, one under the other. The caller makes
// one of them the primary action (the default button variant) and the rest outline or ghost.
</script>

<template>
  <div data-slot="task-actions" class="flex flex-col gap-2 *:w-full">
    <slot />
  </div>
</template>
