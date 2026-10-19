<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The text of a setting row: the title with a small hint (the default) at its right end, and the help line below.
// The hint sits on the title line so a long help line never wraps because of it.
defineProps<{
  title: string;
  titleId?: string;
  description?: string;
  hint?: string;
}>();
</script>

<template>
  <span class="block min-w-0">
    <span :id="titleId" data-row-title class="block text-sm font-medium wrap-break-word">{{
      title
    }}</span>
    <span v-if="description" data-row-description class="block text-sm text-muted-foreground">{{
      description
    }}</span>
    <span v-if="hint" data-row-hint class="block text-xs text-muted-foreground">{{ hint }}</span>
  </span>
</template>
