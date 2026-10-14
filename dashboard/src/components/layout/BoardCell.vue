<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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

const shows = inject(boardKey, () =>
  true);

// The span applies from laptop width; narrower, the board is one column and every cell takes a full row.
const placement = computed<Record<string, string>>(() => ({
  '--cell-span': `span ${props.span}`,
  '--cell-rows': `span ${props.rows}`,
}));
</script>

<template>
  <div
    v-show="shows(panel)"
    data-slot="board-cell"
    :data-panel="panel"
    class="flex min-h-0 min-w-0 flex-1 flex-col *:min-h-0 *:flex-1 lg:col-(--cell-span) lg:row-(--cell-rows)"
    :style="placement"
  >
    <slot />
  </div>
</template>
