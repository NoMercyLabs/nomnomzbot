<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One cell of a BoardGrid: span = columns of 12, rows = rows it covers.
import { computed, type ComputedRef } from 'vue'

const props = withDefaults(defineProps<{ span?: number; rows?: number }>(), { span: 12, rows: 1 })

const placement: ComputedRef<Record<string, string>> = computed<Record<string, string>>(() => ({
  gridColumn: `span ${props.span}`,
  gridRow: `span ${props.rows}`,
}))
</script>

<template>
  <div
    data-slot="board-cell"
    class="flex min-h-0 min-w-0 flex-col *:min-h-0 *:flex-1"
    :style="placement"
  >
    <slot />
  </div>
</template>
