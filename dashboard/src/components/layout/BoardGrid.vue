<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The board layout (C1): from laptop width, a fixed 12-column grid whose rows split the height. The page
// never grows; each BoardCell holds a panel and the panel body scrolls. On a phone a scroll inside a scroll
// is bad, so the board shows one panel at a time, full height, behind a tab per panel (?panel=).
import { useMediaQuery } from '@vueuse/core';
import { computed, provide } from 'vue';

import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { usePageView } from '@/composables/usePageView';

import { boardKey, type BoardPanel } from './board';

const props = withDefaults(defineProps<{
  panels: readonly BoardPanel[];
  rows?: number;
}>(), { rows: 2 });

const rowTemplate = computed<string>(() => `repeat(${props.rows}, minmax(0, 1fr))`);
const wide = useMediaQuery('(min-width: 1024px)');
const tabbed = computed(() => !wide.value && props.panels.length > 1);
const open = usePageView(() => props.panels.map((panel) =>
  panel.id), 'panel');

provide(boardKey, (panel: string) =>
  !tabbed.value || open.value === panel);
</script>

<template>
  <section
    v-if="tabbed"
    data-slot="board-grid"
    data-page-body
    class="flex min-h-0 flex-1 flex-col gap-3 pt-3 pb-4"
  >
    <Tabs v-model="open" class="min-w-0 flex-none overflow-x-auto px-4 scrollbar-none">
      <TabsList>
        <TabsTrigger v-for="panel in panels" :key="panel.id" :value="panel.id">
          {{ panel.label }}
        </TabsTrigger>
      </TabsList>
    </Tabs>
    <div class="flex min-h-0 flex-1 flex-col px-4">
      <slot />
    </div>
  </section>
  <section
    v-else
    data-slot="board-grid"
    data-page-body
    class="grid min-h-0 flex-1 auto-rows-[minmax(20rem,auto)] grid-cols-1 gap-4 overflow-y-auto px-4 pt-4
      pb-6 lg:grid-cols-12 lg:grid-rows-(--board-rows) lg:overflow-visible lg:px-6"
    :style="{ '--board-rows': rowTemplate }"
  >
    <slot />
  </section>
</template>
