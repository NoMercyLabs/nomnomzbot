<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The board layout (C1): a fixed 12-column grid whose rows split the height. The page never grows;
// each BoardCell holds a panel and the panel body scrolls.
import { computed, type ComputedRef } from 'vue'

const props = withDefaults(defineProps<{ rows?: number }>(), { rows: 2 })

const rowTemplate: ComputedRef<string> = computed<string>(() => `repeat(${props.rows}, minmax(0, 1fr))`)
</script>

<template>
  <section
    data-slot="board-grid"
    class="grid min-h-0 flex-1 grid-cols-12 gap-4 px-6 pt-4 pb-6"
    :style="{ gridTemplateRows: rowTemplate }"
  >
    <slot />
  </section>
</template>
