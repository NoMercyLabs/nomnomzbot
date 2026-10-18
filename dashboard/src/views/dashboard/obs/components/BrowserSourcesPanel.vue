<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Every overlay is one browser source in OBS. Its dot is the one the Overlays list shows (overlayHealth):
// green when a source is open, orange when switched on but nothing is attached, red when its last run failed.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { Widget } from '@/api/widgets';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
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
      </SummaryRow>
    </div>
  </PanelCard>
</template>
