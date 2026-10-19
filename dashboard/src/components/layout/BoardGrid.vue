<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts" generic="Id extends string">
// The board layout (C1): from laptop width, a fixed 12-column grid whose rows split the height. The page
// never grows; each BoardCell holds a panel and the panel body scrolls. On a phone a scroll inside a scroll
// is bad, so the board shows one panel at a time, full height, behind a tab per panel (?panel=).
import { computed, provide, useTemplateRef } from 'vue';

import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { type Board, useBoard } from '@/composables/useBoard';
import { usePageView } from '@/composables/usePageView';
import { useScrollFade } from '@/composables/useScrollFade';

import { boardKey, type BoardPanel } from './board';

const props = withDefaults(
  defineProps<{
    panels: readonly BoardPanel<Id>[];
    rows?: number;
    // A page with an optional panel builds its own board (useBoard on this component's ref) to know the span and
    // to draw the drawer; every other page lets the grid count its columns itself.
    board?: Board;
  }>(),
  {
    rows: 2,
    board: undefined,
  },
);

const rowTemplate = computed<string>(() => `repeat(${props.rows}, minmax(0, 1fr))`);
// Stoney 2026-10-14: "constrained layouts need to be considered for". The sidebar takes its share of the window,
// so the board measures itself and counts columns from what its panels declare they need (minWidth).
const root = useTemplateRef('root');
const strip = useTemplateRef('strip');
const fade = useScrollFade(() => strip.value?.$el);
const state = props.board ?? useBoard('board', root, () => props.panels);
const wide = state.wide;
const tabbed = state.tabbed;
const tabPanels = computed(() => props.panels.filter((panel) => !panel.optional));

const open = usePageView(() => tabPanels.value.map((panel) => panel.id), 'panel');

provide(boardKey, (panel: string) =>
  !tabbed.value || open.value === panel);
</script>

<template>
  <div ref="root" class="flex min-h-0 min-w-0 flex-1 flex-col">
    <section
      v-if="tabbed"
      data-slot="board-grid"
      data-page-body
      class="flex min-h-0 flex-1 flex-col gap-3 pt-3 pb-4"
    >
      <Tabs
        ref="strip"
        v-model="open"
        class="min-w-0 flex-none overflow-x-auto overflow-y-hidden px-6 scrollbar-none"
        :class="fade"
      >
        <TabsList>
          <TabsTrigger v-for="panel in tabPanels" :key="panel.id" :value="panel.id">
            {{ panel.label }}
          </TabsTrigger>
        </TabsList>
      </Tabs>
      <div class="flex min-h-0 flex-1 flex-col px-6">
        <slot />
      </div>
    </section>
    <section
      v-else
      data-slot="board-grid"
      data-page-body
      :data-board-wide="wide || undefined"
      class="grid min-h-0 flex-1 auto-rows-[minmax(20rem,auto)] grid-cols-1 gap-4 overflow-y-auto px-6 pt-4 pb-6 data-board-wide:grid-cols-12 data-board-wide:grid-rows-(--board-rows) data-board-wide:overflow-visible"
      :style="{ '--board-rows': rowTemplate }"
    >
      <slot />
    </section>
  </div>
</template>
