<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The board layout (C1): from laptop width, a fixed 12-column grid whose rows split the height. The page
// never grows; each BoardCell holds a panel and the panel body scrolls. Narrower, the panels stack in one
// column at a readable height and the board scrolls instead.
import { computed } from 'vue';

const props = withDefaults(defineProps<{ rows?: number }>(), { rows: 2 });

const rowTemplate = computed<string>(() => `repeat(${props.rows}, minmax(0, 1fr))`);
</script>

<template>
  <section
    data-slot="board-grid"
    data-page-body
    class="grid min-h-0 flex-1 auto-rows-[minmax(20rem,auto)] grid-cols-1 gap-4 overflow-y-auto px-4 pt-4
      pb-6 lg:grid-cols-12 lg:grid-rows-(--board-rows) lg:overflow-visible lg:px-6"
    :style="{ '--board-rows': rowTemplate }"
  >
    <slot />
  </section>
</template>
