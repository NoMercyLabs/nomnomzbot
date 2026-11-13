<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One PanelCard in each of its states and tiers.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';

const { t } = useI18n();

const retries = ref(0);
</script>

<template>
  <section
    data-catalog="panel-states"
    class="grid grid-cols-2 gap-4 md:h-72 xl:grid-cols-3 max-md:grid-cols-1"
  >
    <PanelCard :title="t('dashboard.catalog.panel.ready')" tier="lead">
      <RowGroup>
        <SummaryRow
          :title="t('dashboard.catalog.panel.readyRow')"
          :detail="t('dashboard.catalog.panel.readyDetail')"
        />
      </RowGroup>
    </PanelCard>
    <PanelCard :title="t('dashboard.catalog.panel.loading')" state="loading" />
    <PanelCard
      :title="t('dashboard.catalog.panel.empty')"
      state="empty"
      :empty-text="t('dashboard.catalog.panel.emptyText')"
    />
    <PanelCard
      :title="t('dashboard.catalog.panel.error')"
      state="error"
      :description="t('dashboard.catalog.panel.retried', { count: retries })"
      @retry="retries += 1"
    />
    <PanelCard :title="t('dashboard.catalog.panel.quiet')" tier="quiet" />
  </section>
</template>
