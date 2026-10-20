<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
