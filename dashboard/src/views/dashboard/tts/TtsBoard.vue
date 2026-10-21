<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The panels of one TTS tab. From md the board lays them out in its grid; a phone gets them stacked in the one
// page scroll, so the page keeps a single tab band and no second panel picker (Stoney 2026-10-21).
import { computed, type VNode } from 'vue';

import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import type { BoardPanel } from '@/components/layout/board';
import { useScreenFrom } from '@/composables/useLayout';
import { merge } from '@/lib/array';

const props = defineProps<{
  panels: readonly BoardPanel[];
  // The grid span (of 12) of each panel, by panel id.
  spans: Record<string, number>;
  rows: number;
  // The panel that opens the phone stack when it is not the first of the grid: a short panel goes above a
  // long paged list, so it is not buried under it.
  phoneFirst?: string;
}>();

defineSlots<Record<string, () => VNode[]>>();

const tablet = useScreenFrom('tablet');
const stacked = computed<readonly BoardPanel[]>(() =>
  merge(
    props.panels.filter((panel) => panel.id === props.phoneFirst),
    props.panels.filter((panel) => panel.id !== props.phoneFirst),
  ));
</script>

<template>
  <div
    v-if="!tablet"
    data-slot="tts-stack"
    data-page-body
    class="flex flex-col gap-4 px-4 pt-3 pb-4 *:shrink-0"
  >
    <slot v-for="panel in stacked" :key="panel.id" :name="panel.id" />
  </div>
  <BoardGrid v-else :panels="panels" :rows="rows">
    <BoardCell v-for="panel in panels" :key="panel.id" :panel="panel.id" :span="spans[panel.id]">
      <slot :name="panel.id" />
    </BoardCell>
  </BoardGrid>
</template>
