<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The board grid and the list-and-detail layout.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import type { BoardPanel } from '@/components/layout/board';
import ListDetail from '@/components/layout/ListDetail.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';

const { t } = useI18n();

const boardPanels = computed(() => [
  { id: 'board', label: t('dashboard.catalog.layout.board') },
  { id: 'cell', label: t('dashboard.catalog.layout.cell') },
  { id: 'quiet', label: t('dashboard.catalog.panel.quiet') },
] as const satisfies readonly BoardPanel[]);

const detailOpen = ref(false);
</script>

<template>
  <section data-catalog="layouts" class="grid gap-4">
    <div class="flex flex-col md:h-80">
      <BoardGrid :panels="boardPanels">
        <BoardCell panel="board" :span="7" :rows="2">
          <PanelCard :title="t('dashboard.catalog.layout.board')" tier="lead" />
        </BoardCell>
        <BoardCell panel="cell" :span="5">
          <PanelCard :title="t('dashboard.catalog.layout.cell')" />
        </BoardCell>
        <BoardCell panel="quiet" :span="5">
          <PanelCard :title="t('dashboard.catalog.layout.cell')" tier="quiet" />
        </BoardCell>
      </BoardGrid>
    </div>
    <div class="flex flex-col md:h-80">
      <ListDetail :show-detail="detailOpen">
        <template #list>
          <PanelCard :title="t('dashboard.catalog.layout.list')">
            <RowGroup>
              <SummaryRow
                pick
                :title="t('dashboard.catalog.panel.readyRow')"
                @click="detailOpen = !detailOpen"
              />
            </RowGroup>
          </PanelCard>
        </template>
        <template #detail>
          <PanelCard :title="t('dashboard.catalog.layout.detail')" tier="lead" />
        </template>
        <template #aside>
          <PanelCard :title="t('dashboard.catalog.layout.aside')" tier="quiet" />
        </template>
      </ListDetail>
    </div>
  </section>
</template>
