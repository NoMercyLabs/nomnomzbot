<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One cell of a BoardGrid: panel = the board panel id it holds, span = columns of 12, rows = rows it covers.
import { computed, inject } from 'vue';

import { boardKey } from './board';

const props = withDefaults(defineProps<{
  panel: string;
  span?: number;
  rows?: number;
}>(), {
  span: 12,
  rows: 1,
});

const shows = inject(boardKey, () => true);

// The span applies on a board wide enough for its columns; narrower, the board shows one panel at a time.
const placement = computed(() => ({
  '--cell-span': `span ${props.span}`,
  '--cell-rows': `span ${props.rows}`,
}) as const satisfies Record<string, string>);
</script>

<template>
  <div
    v-show="shows(panel)"
    data-slot="board-cell"
    :data-panel="panel"
    class="flex min-h-0 min-w-0 flex-1 flex-col *:min-h-0 *:flex-1 in-data-board-wide:col-(--cell-span) in-data-board-wide:row-(--cell-rows)"
    :style="placement"
  >
    <slot />
  </div>
</template>
