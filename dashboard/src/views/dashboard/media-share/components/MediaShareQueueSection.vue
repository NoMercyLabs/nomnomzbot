<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One group of the queue: a quiet heading with the item count, then its rows.
defineProps<{
  kind: string;
  title: string;
  count: number;
}>();
</script>

<template>
  <section :data-queue-section="kind" class="flex flex-col gap-1 not-first:mt-3">
    <h3 class="flex items-center gap-2 px-3 pt-1 text-sm font-medium text-muted-foreground">
      {{ title }}
      <span class="text-xs tabular-nums">{{ count }}</span>
    </h3>
    <slot />
  </section>
</template>
