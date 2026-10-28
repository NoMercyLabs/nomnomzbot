<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The channel point rewards, one switch each: on, the bot reads the viewer's own text of that reward aloud.
// A switch saves at once and sends only that field.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useRewards, useUpdateReward } from '@/api/rewards';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import type { PanelState } from '@/composables/usePanelState';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const rewards = useRewards(channelId);
const update = useUpdateReward(channelId);

const list = computed(() => rewards.data.value ?? []);

const state = computed<PanelState>(() => {
  if (rewards.isError.value)
    return 'error';

  if (rewards.isPending.value)
    return 'loading';

  return list.value.length === 0
    ? 'empty'
    : 'ready';
});

function cost(count: number | string): string {
  return t('dashboard.tts.rewards.cost', { count: String(count) });
}

function speak(rewardId: string, speaksViaTts: boolean): void {
  update.mutate({ rewardId, change: { speaksViaTts } });
}
</script>

<template>
  <PanelCard
    data-view="tts-rewards"
    pad
    :title="$t('dashboard.tts.rewards.title')"
    :description="$t('dashboard.tts.rewards.description')"
    :state="state"
    :empty-text="$t('dashboard.tts.rewards.empty')"
    :error-text="$t('dashboard.tts.settings.error')"
    @retry="rewards.refetch()"
  >
    <RowGroup>
      <SettingRow
        v-for="reward in list"
        :key="reward.id"
        :model-value="reward.speaksViaTts"
        :disabled="!can('tts:config:write')"
        :title="reward.title"
        :description="cost(reward.cost)"
        @update:model-value="speak(reward.id, $event)"
      />
    </RowGroup>
  </PanelCard>
</template>
