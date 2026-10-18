<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The voice the built-in commands answer in. The bot lists the tones it accepts; a pick is saved at once
// and shows at once, and it rolls back with a message in the error inbox when the bot refuses it.
import type { AcceptableValue } from 'reka-ui';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePersonality, useSetPersonality } from '@/api/personalitySettings';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Label } from '@/components/ui/label';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

interface ToneChoice {
  value: string;
  label: string;
  description: string | null;
}

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const personality = usePersonality(channelId);
const save = useSetPersonality(channelId);
const state = usePanelState(personality);

// A tone this dashboard has no words for yet still shows, under its own token, so it can be picked.
const choices = computed<ToneChoice[]>(
  () => (personality.data.value?.available ?? []).map((value: string) => ({
    value,
    label: te(`dashboard.settings.personality.tones.${value}.title`)
      ? t(`dashboard.settings.personality.tones.${value}.title`)
      : value,
    description: te(`dashboard.settings.personality.tones.${value}.description`)
      ? t(`dashboard.settings.personality.tones.${value}.description`)
      : null,
  })),
);

function pick(value: AcceptableValue): void {
  if (typeof value === 'string' && value !== personality.data.value?.personality) {
    save.mutate(value);
  }
}
</script>

<template>
  <PanelCard
    data-view="personality"
    pad
    :state="state"
    :title="$t('dashboard.settings.personality.title')"
    :description="$t('dashboard.settings.personality.description')"
    :error-text="$t('dashboard.settings.personality.error')"
    @retry="personality.refetch()"
  >
    <RadioGroup
      class="gap-0"
      :disabled="!can('setup:write')"
      :model-value="personality.data.value?.personality"
      :aria-label="$t('dashboard.settings.personality.title')"
      @update:model-value="pick"
    >
      <RowGroup>
        <Label
          v-for="choice in choices"
          :key="choice.value"
          :data-tone="choice.value"
          class="flex min-h-11 cursor-pointer items-center gap-3 rounded-[8px] px-3 py-2 font-normal hover:bg-foreground/5"
        >
          <RadioGroupItem :value="choice.value" />
          <span class="min-w-0 flex-1">
            <span class="block text-sm font-medium">{{ choice.label }}</span>
            <span v-if="choice.description" class="block text-sm text-muted-foreground">{{
              choice.description
            }}</span>
          </span>
        </Label>
      </RowGroup>
    </RadioGroup>
    <template #footer>
      <span class="text-sm text-muted-foreground">{{
        $t('dashboard.settings.personality.footer')
      }}</span>
    </template>
  </PanelCard>
</template>
