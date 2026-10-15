<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The scenes OBS reports, with the one on air marked. Read only: the page shows state, it never switches a scene.
import { computed } from 'vue';

import type { ObsScene } from '@/api/obs';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';

const props = defineProps<{
  scenes: readonly ObsScene[] | undefined;
  panelState: PanelState;
  // OBS is off or did not answer: the panel says so instead of an empty list.
  offline: boolean;
}>();

defineEmits<{ retry: [] }>();

const state = computed<PanelState>(() => {
  if (props.offline)
    return 'empty';

  return props.panelState === 'ready' && props.scenes?.length === 0
    ? 'empty'
    : props.panelState;
});
</script>

<template>
  <PanelCard
    :title="$t('dashboard.obs.scenes.title')"
    :state="state"
    :empty-text="offline ? $t('dashboard.obs.scenes.offline') : $t('dashboard.obs.scenes.empty')"
    :error-text="$t('dashboard.obs.scenes.loadError')"
    @retry="$emit('retry')"
  >
    <div class="px-1 py-0.5">
      <SummaryRow
        v-for="scene in scenes"
        :key="scene.name"
        data-obs-scene
        :data-current="scene.isCurrent"
        :title="scene.name"
        :detail="scene.isCurrent ? $t('dashboard.obs.scenes.current') : undefined"
      >
        <template #detail-lead>
          <span class="dot ok" aria-hidden="true" />
        </template>
      </SummaryRow>
    </div>
  </PanelCard>
</template>
