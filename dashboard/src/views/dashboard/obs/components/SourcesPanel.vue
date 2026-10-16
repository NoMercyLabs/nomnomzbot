<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The sources in the scene on air. Each one can be shown or hidden, locked or unlocked, and opened to switch its filters.
import { computed, ref } from 'vue';
import { toastObsError } from '../obsToast';
import ChevronDown from '~icons/nnz/chevron-down';
import ChevronRight from '~icons/nnz/chevron-right';
import ClosedEye from '~icons/nnz/closed-eye';
import Eye from '~icons/nnz/eye';
import Locked from '~icons/nnz/locked';
import UnLocked from '~icons/nnz/un-locked';

import { useObsSceneItemFlag, useObsSceneItems, type ObsSceneItem } from '@/api/obsSources';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import SourceFilters from './SourceFilters.vue';

const props = defineProps<{
  // The scene on air; null while OBS has not said which one.
  sceneName: string | null;
  offline: boolean;
}>();

const { channelId } = useCurrentChannel();
const scene = computed(() =>
  props.offline
    ? null
    : props.sceneName);
const items = useObsSceneItems(channelId, scene);
const loaded = usePanelState(items);
const setEnabled = useObsSceneItemFlag(channelId, 'enabled');
const setLocked = useObsSceneItemFlag(channelId, 'locked');
const open = ref<number | null>(null);

const showError = (error: Error): void => {
  toastObsError(error);
};

function toggleShown(item: ObsSceneItem): void {
  if (scene.value === null) return;

  setEnabled.mutate({
    sceneName: scene.value,
    sceneItemId: Number(item.sceneItemId),
    value: !item.enabled,
  }, { onError: showError });
}

function toggleLocked(item: ObsSceneItem): void {
  if (scene.value === null) return;

  setLocked.mutate({
    sceneName: scene.value,
    sceneItemId: Number(item.sceneItemId),
    value: !item.locked,
  }, { onError: showError });
}

function toggleOpen(item: ObsSceneItem): void {
  const id = Number(item.sceneItemId);

  open.value = open.value === id
    ? null
    : id;
}

const state = computed<PanelState>(() => {
  if (scene.value === null)
    return 'empty';

  return loaded.value === 'ready' && items.data.value?.length === 0
    ? 'empty'
    : loaded.value;
});
</script>

<template>
  <PanelCard
    :title="$t('dashboard.obs.sourceItems.title')"
    :state="state"
    :empty-text="scene === null ? $t('dashboard.obs.sourceItems.offline') : $t('dashboard.obs.sourceItems.empty')"
    :error-text="$t('dashboard.obs.sourceItems.loadError')"
    @retry="items.refetch()"
  >
    <div class="px-1 py-0.5">
      <template v-for="item in items.data.value" :key="item.sceneItemId">
        <SummaryRow
          data-obs-scene-item
          :data-source="item.sourceName"
          :title="item.sourceName"
          :detail="item.enabled ? undefined : $t('dashboard.obs.sourceItems.hidden')"
        >
          <template #end>
            <Button
              data-source-shown
              variant="ghost"
              size="icon"
              :aria-pressed="item.enabled"
              :aria-label="$t('dashboard.obs.sourceItems.shown', { name: item.sourceName })"
              @click="toggleShown(item)"
            >
              <Eye v-if="item.enabled" class="size-5" aria-hidden="true" />
              <ClosedEye v-else class="size-5 text-muted-foreground" aria-hidden="true" />
            </Button>
            <Button
              data-source-locked
              variant="ghost"
              size="icon"
              :aria-pressed="item.locked"
              :aria-label="$t('dashboard.obs.sourceItems.locked', { name: item.sourceName })"
              @click="toggleLocked(item)"
            >
              <Locked v-if="item.locked" class="size-5" aria-hidden="true" />
              <UnLocked v-else class="size-5 text-muted-foreground" aria-hidden="true" />
            </Button>
            <Button
              data-source-filters-toggle
              variant="ghost"
              size="icon"
              :aria-expanded="open === Number(item.sceneItemId)"
              :aria-label="$t('dashboard.obs.sourceItems.filters', { name: item.sourceName })"
              @click="toggleOpen(item)"
            >
              <ChevronDown v-if="open === Number(item.sceneItemId)" class="size-5" aria-hidden="true" />
              <ChevronRight v-else class="size-5" aria-hidden="true" />
            </Button>
          </template>
        </SummaryRow>
        <SourceFilters v-if="open === Number(item.sceneItemId)" :source-name="item.sourceName" />
      </template>
    </div>
  </PanelCard>
</template>
