<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The scenes OBS reports, with the one on air marked. A click puts that scene on air.
import { computed } from 'vue';

import { useObsSwitchScene, type ObsScene } from '@/api/obs';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { toastObsError } from '../obsToast';

const props = defineProps<{
  scenes: readonly ObsScene[] | undefined;
  panelState: PanelState;
  // OBS is off or did not answer: the panel says so instead of an empty list.
  offline: boolean;
}>();

defineEmits<{ retry: [] }>();

const { channelId } = useCurrentChannel();
const switchScene = useObsSwitchScene(channelId);

function switchTo(scene: string): void {
  switchScene.mutate(scene, { onError: toastObsError });
}

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
        pick
        :selected="scene.isCurrent"
        :data-current="scene.isCurrent"
        @click="switchTo(scene.name)"
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
