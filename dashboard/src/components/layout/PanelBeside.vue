<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The row a toggled SidePanel opens into. The page content takes the first column; the panel slot takes a third
// column beside it where the window is wide (SidePanel decides), and is a sheet over the page where it is not.
// Stoney 2026-10-20: "the request rules pannel proably fits just fine as a 3rd  column on this breakpont and must
// be consistent with the other pannel behaviors."
</script>

<template>
  <div
    data-slot="panel-beside"
    class="flex min-h-0 min-w-0 flex-1 *:data-[slot=side-panel]:mt-6 *:data-[slot=side-panel]:mr-6 *:data-[slot=side-panel]:mb-6"
  >
    <div data-slot="panel-beside-content" class="flex min-h-0 min-w-0 flex-1 flex-col">
      <slot />
    </div>
    <slot name="panel" />
  </div>
</template>
