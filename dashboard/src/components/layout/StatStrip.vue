<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A row of StatTile that wraps by width, so four tiles stay four on a wide window and stack on a phone.
</script>

<template>
  <div data-slot="stat-strip" class="grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-4">
    <slot />
  </div>
</template>
