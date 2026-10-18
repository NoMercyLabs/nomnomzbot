<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The rules of the channel voice: the follow-the-platform switch, who may speak and the bits gate.
// Each switch saves at once; the bits box saves when it loses focus or Enter is pressed.
import { computed, ref, watch } from 'vue';

import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import ReadabilityRow from '@/views/dashboard/settings/components/ReadabilityRow.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useTtsConfig } from '@/api/tts';
import { useSetTtsRules, type TtsRulesChange } from '@/api/ttsRules';

const { channelId } = useCurrentChannel();
const { can } = useCan();
const config = useTtsConfig(channelId);
const save = useSetTtsRules(channelId);

const stored = computed(() => config.data.value ?? null);
const bits = ref('');

watch(
  stored,
  (current) => {
    bits.value = String(current?.minBitsToTts ?? 0);
  },
  { immediate: true },
);

const state = computed<PanelState>(() => {
  if (config.isError.value)
    return 'error';

  return config.isPending.value
    ? 'loading'
    : 'ready';
});

function change(rules: TtsRulesChange): void {
  save.mutate(rules);
}

function commitBits(): void {
  const parsed = Math.trunc(Number(bits.value));
  const next = Number.isFinite(parsed)
    ? Math.max(0, parsed)
    : 0;

  bits.value = String(next);

  if (next !== Number(stored.value?.minBitsToTts ?? 0)) {
    change({ minBitsToTts: next });
  }
}
</script>

<template>
  <PanelCard
    data-view="tts-rules"
    pad
    :title="$t('dashboard.tts.rules.title')"
    :description="$t('dashboard.tts.rules.description')"
    :state="state"
    :error-text="$t('dashboard.tts.settings.error')"
    @retry="config.refetch()"
  >
    <RowGroup v-if="stored">
      <SettingRow
        :model-value="stored.followsPlatformDefaultVoice"
        :disabled="!can('tts:config:write')"
        :title="$t('dashboard.tts.rules.follow.title')"
        :description="$t('dashboard.tts.rules.follow.description')"
        @update:model-value="change({ followPlatformDefaultVoice: $event })"
      />
      <SettingRow
        :model-value="stored.readUsernames"
        :disabled="!can('tts:config:write')"
        :title="$t('dashboard.tts.rules.readUsernames.title')"
        :description="$t('dashboard.tts.rules.readUsernames.description')"
        @update:model-value="change({ readUsernames: $event })"
      />
      <SettingRow
        :model-value="stored.modApprovalRequired"
        :disabled="!can('tts:config:write')"
        :title="$t('dashboard.tts.rules.modApproval.title')"
        :description="$t('dashboard.tts.rules.modApproval.description')"
        @update:model-value="change({ modApprovalRequired: $event })"
      />
      <SettingRow
        :model-value="stored.viewerVoiceSelfServiceEnabled"
        :disabled="!can('tts:config:write')"
        :title="$t('dashboard.tts.rules.selfService.title')"
        :description="$t('dashboard.tts.rules.selfService.description')"
        @update:model-value="change({ viewerVoiceSelfServiceEnabled: $event })"
      />
      <ReadabilityRow
        :title="$t('dashboard.tts.rules.minBits.title')"
        :description="$t('dashboard.tts.rules.minBits.description')"
      >
        <Input
          v-model="bits"
          type="number"
          min="0"
          class="w-24"
          :disabled="!can('tts:config:write')"
          :aria-label="$t('dashboard.tts.rules.minBits.title')"
          @blur="commitBits"
          @keydown.enter="commitBits"
        />
      </ReadabilityRow>
    </RowGroup>
  </PanelCard>
</template>
