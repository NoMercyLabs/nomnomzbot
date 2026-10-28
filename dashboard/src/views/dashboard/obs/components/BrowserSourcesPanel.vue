<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Every overlay is one browser source in OBS. Its dot is the one the Overlays list shows (overlayHealth):
// green when a source is open, orange when switched on but nothing is attached, red when its last run failed.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { Widget } from '@/api/widgets';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import ObsSetupAction from '@/views/dashboard/overlays/components/ObsSetupAction.vue';
import { overlayHealth } from '@/views/dashboard/overlays/components/overlayHealth';

import { sourceReasonKey } from '../obsHealth';

const props = defineProps<{ widgets: readonly Widget[] | undefined; panelState: PanelState }>();

defineEmits<{ retry: [] }>();

const { t } = useI18n();

function reasonOf(widget: Widget): string {
  return t(sourceReasonKey(widget));
}

const state = computed<PanelState>(() =>
  props.panelState === 'ready' && props.widgets?.length === 0
    ? 'empty'
    : props.panelState,
);
</script>

<template>
  <PanelCard
    :title="$t('dashboard.obs.sources.title')"
    :description="widgets ? $t('dashboard.obs.sources.count', { count: widgets.length }) : undefined"
    :state="state"
    :empty-text="$t('dashboard.obs.sources.empty')"
    :error-text="$t('dashboard.obs.sources.loadError')"
    @retry="$emit('retry')"
  >
    <div class="px-1 py-0.5">
      <SummaryRow
        v-for="widget in widgets"
        :key="widget.id"
        data-obs-source
        :title="widget.name"
        :detail="reasonOf(widget)"
        :meta="widget.isEnabled ? (widget.lastRuntimeError ?? undefined) : undefined"
      >
        <template #detail-lead>
          <span data-obs-dot="source" class="dot" :class="overlayHealth(widget)" aria-hidden="true" />
        </template>
        <template #trailing>
          <ObsSetupAction
            :title="widget.name"
            :setup="widget.obsSetup"
            :connected="widget.isAttached"
            :expected="widget.isEnabled"
            :disabled="!widget.overlayUrl"
            :resolve-address="async () => widget.overlayUrl"
          />
        </template>
      </SummaryRow>
    </div>
  </PanelCard>
</template>
