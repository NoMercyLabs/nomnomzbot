<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The hotkeys OBS knows. One click fires the hotkey by its name.
import { computed } from 'vue';

import { useObsHotkeys, useObsTriggerHotkey } from '@/api/obsControl';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { toastObsError } from '../obsToast';

const props = defineProps<{ offline: boolean }>();

const { channelId } = useCurrentChannel();
const connected = computed<boolean>(() => !props.offline);
const hotkeys = useObsHotkeys(channelId, connected);
const trigger = useObsTriggerHotkey(channelId);
const loaded = usePanelState(hotkeys);

const state = computed(() => {
  if (props.offline)
    return 'empty';

  return loaded.value === 'ready' && hotkeys.data.value?.length === 0
    ? 'empty'
    : loaded.value;
});

function fire(hotkeyName: string): void {
  trigger.mutate(hotkeyName, { onError: toastObsError });
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.obs.hotkeys.title')"
    :state="state"
    :empty-text="offline ? $t('dashboard.obs.hotkeys.offline') : $t('dashboard.obs.hotkeys.empty')"
    :error-text="$t('dashboard.obs.hotkeys.loadError')"
    @retry="hotkeys.refetch()"
  >
    <div class="flex flex-wrap gap-2 p-3">
      <Button
        v-for="name in hotkeys.data.value"
        :key="name"
        variant="outline"
        :data-obs-hotkey="name"
        @click="fire(name)"
      >
        {{ name }}
      </Button>
    </div>
  </PanelCard>
</template>
