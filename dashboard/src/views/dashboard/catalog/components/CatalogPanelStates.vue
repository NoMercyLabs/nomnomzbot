<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One PanelCard in each of its states and tiers: ready, loading, empty, error, quiet, and the two states a page
// sets itself, read-only (ReadOnlyScope with its reason) and destructive (its own button variant, the
// consequence in words). No-match and gated are the EmptyState kinds on the Feedback tab.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Switch } from '@/components/ui/switch';

const { t } = useI18n();

const retries = ref(0);
</script>

<template>
  <section
    data-catalog="panel-states"
    class="grid grid-cols-2 gap-4 md:auto-rows-44 xl:grid-cols-3 max-md:grid-cols-1"
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
    <PanelCard data-state-demo="read-only" :title="t('dashboard.catalog.panel.readOnly')" pad>
      <ReadOnlyScope read-only :reason="t('dashboard.catalog.panel.readOnlyReason')">
        <label class="flex items-center gap-2 text-sm">
          <Switch :aria-label="t('dashboard.catalog.panel.readOnlySwitch')" />
          {{ t('dashboard.catalog.panel.readOnlySwitch') }}
        </label>
      </ReadOnlyScope>
    </PanelCard>
    <PanelCard data-state-demo="destructive" :title="t('dashboard.catalog.panel.destructive')" pad>
      <div class="flex flex-col items-start gap-3">
        <p class="text-sm text-muted-foreground">{{ t('dashboard.catalog.panel.destructiveText') }}</p>
        <Button variant="destructive" size="sm">{{ t('dashboard.catalog.panel.destructiveAction') }}</Button>
      </div>
    </PanelCard>
  </section>
</template>
