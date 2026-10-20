<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One cap that applies to a single message: its name and its value. There is nothing to use up over time, so
// there is no bar.
defineProps<{
  label: string;
  value: string;
}>();
</script>

<template>
  <div data-limit-row class="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 px-3 py-2.5">
    <span class="min-w-0 flex-1 basis-48 text-sm font-medium">{{ label }}</span>
    <span class="text-sm tabular-nums text-muted-foreground">{{ value }}</span>
  </div>
</template>
