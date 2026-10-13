<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
