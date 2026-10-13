<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One option of the Readability view: title and help on the left, the control on the right.
defineProps<{ title: string; description?: string }>();
</script>

<template>
  <div class="flex min-h-11 flex-wrap items-center gap-x-4 gap-y-2 px-3 py-2">
    <div class="min-w-0 flex-1 basis-56">
      <span class="block text-sm font-medium">{{ title }}</span>
      <span v-if="description" class="block text-sm text-muted-foreground">{{ description }}</span>
    </div>
    <div class="flex items-center gap-3"><slot /></div>
  </div>
</template>
