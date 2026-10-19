<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The rules of the channel voice: the follow-the-platform switch, who may speak, the bot and profanity filters and the bits gate.
// Each switch saves at once; the bits box saves when it loses focus or Enter is pressed.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import ReadabilityRow from '@/views/dashboard/settings/components/ReadabilityRow.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useNumberField } from '@/composables/useNumberField';
import { useTtsConfig } from '@/api/tts';
import { useSetTtsRules, useTtsDefaults, type TtsRulesChange } from '@/api/ttsRules';

import TtsResetFooter from './TtsResetFooter.vue';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const config = useTtsConfig(channelId);
const defaults = useTtsDefaults(channelId);
const save = useSetTtsRules(channelId);

const stored = computed(() => config.data.value ?? null);

const state = computed<PanelState>(() => {
  if (config.isError.value)
    return 'error';

  return config.isPending.value
    ? 'loading'
    : 'ready';
});

// The bot's own default, shown beside the title, so a streamer sees what a reset returns to.
function switchHint(standard: boolean | undefined): string {
  if (standard === undefined)
    return '';

  return t(standard
    ? 'dashboard.tts.defaultHint.on'
    : 'dashboard.tts.defaultHint.off');
}

const bitsHint = computed<string>(() => {
  const standard = defaults.data.value;

  return standard
    ? t('dashboard.tts.defaultHint.value', { value: String(standard.minBitsToTts ?? 0) })
    : '';
});

function change(rules: TtsRulesChange): void {
  save.mutate(rules);
}

const bits = useNumberField({
  stored: () => Number(stored.value?.minBitsToTts ?? 0),
  fewest: 0,
  most: () => undefined,
  commit: (next) => change({ minBitsToTts: next }),
});
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
        :hint="switchHint(defaults.data.value?.followsPlatformDefaultVoice)"
        @update:model-value="change({ followPlatformDefaultVoice: $event })"
      />
      <SettingRow
        :model-value="stored.readUsernames"
        :disabled="!can('tts:config:write')"
        :title="$t('dashboard.tts.rules.readUsernames.title')"
        :description="$t('dashboard.tts.rules.readUsernames.description')"
        :hint="switchHint(defaults.data.value?.readUsernames)"
        @update:model-value="change({ readUsernames: $event })"
      />
      <SettingRow
        :model-value="stored.modApprovalRequired"
        :disabled="!can('tts:config:write')"
        :title="$t('dashboard.tts.rules.modApproval.title')"
        :description="$t('dashboard.tts.rules.modApproval.description')"
        :hint="switchHint(defaults.data.value?.modApprovalRequired)"
        @update:model-value="change({ modApprovalRequired: $event })"
      />
      <SettingRow
        :model-value="stored.viewerVoiceSelfServiceEnabled"
        :disabled="!can('tts:config:write')"
        :title="$t('dashboard.tts.rules.selfService.title')"
        :description="$t('dashboard.tts.rules.selfService.description')"
        :hint="switchHint(defaults.data.value?.viewerVoiceSelfServiceEnabled)"
        @update:model-value="change({ viewerVoiceSelfServiceEnabled: $event })"
      />
      <SettingRow
        :model-value="stored.profanityCensorEnabled"
        :disabled="!can('tts:config:write')"
        :title="$t('dashboard.tts.speaking.censor.title')"
        :description="$t('dashboard.tts.speaking.censor.description')"
        :hint="switchHint(defaults.data.value?.profanityCensorEnabled)"
        @update:model-value="change({ profanityCensorEnabled: $event })"
      />
      <ReadabilityRow
        :title="$t('dashboard.tts.rules.minBits.title')"
        :description="$t('dashboard.tts.rules.minBits.description')"
        :hint="bitsHint"
      >
        <Input
          v-model="bits.text.value"
          type="number"
          min="0"
          class="w-24"
          :disabled="!can('tts:config:write')"
          :aria-label="$t('dashboard.tts.rules.minBits.title')"
          @blur="bits.save"
          @keydown.enter="bits.save"
        />
      </ReadabilityRow>
    </RowGroup>

    <template v-if="can('tts:config:write')" #footer>
      <TtsResetFooter />
    </template>
  </PanelCard>
</template>
