<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// How the channel voice speaks: on or off, where the voice comes from, who may be heard, the length cap
// (the bot and profanity filters live in the Message rules panel). Each control names its default; the footer button returns the
// rules to those defaults. Switches and choices save at once; the character cap saves on blur or Enter.
import { computed, ref, watch } from 'vue';
import type { AcceptableValue } from 'reka-ui';
import { useI18n } from 'vue-i18n';

import { useTtsConfig } from '@/api/tts';
import { useSetTtsRules, useTtsDefaults, useTtsOptions, type TtsRulesChange } from '@/api/ttsRules';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import type { PanelState } from '@/composables/usePanelState';
import { useProviderName } from '@/composables/useProviderName';
import ReadabilityRow from '@/views/dashboard/settings/components/ReadabilityRow.vue';

import TtsResetFooter from './TtsResetFooter.vue';

const FEWEST_CHARACTERS = 1;

const { t } = useI18n();
const providerName = useProviderName();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const config = useTtsConfig(channelId);
const defaults = useTtsDefaults(channelId);
const options = useTtsOptions(channelId);
const save = useSetTtsRules(channelId);

const stored = computed(() => config.data.value ?? null);
const standard = computed(() => defaults.data.value ?? null);
const writable = computed(() => can('tts:config:write'));
const characters = ref('');

watch(
  stored,
  (current) => {
    characters.value = String(current?.maxCharacters ?? '');
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

const offered = computed(() => options.data.value ?? null);
const mostCharacters = computed(() => {
  const limit = offered.value?.maxCharactersLimit;

  return limit === undefined
    ? undefined
    : Number(limit);
});

const modes = computed(() =>
  (offered.value?.modes ?? []).map((id) => ({ value: id, label: modeName(id) })),
);

const providers = computed(() =>
  (offered.value?.providers ?? []).map((id) => ({ value: id, label: providerName(id) })),
);

function standingName(id: string): string {
  return t(`dashboard.tts.speaking.standing.${id}`);
}

function modeName(id: string): string {
  return t(`dashboard.tts.speaking.mode.${id}`);
}

function characterCount(count: number | string): string {
  return t('dashboard.tts.speaking.maxCharacters.value', { count: String(count) });
}

function switchHint(value: boolean | undefined): string {
  if (value === undefined)
    return '';

  return t(value
    ? 'dashboard.tts.defaultHint.on'
    : 'dashboard.tts.defaultHint.off');
}

function valueHint(value: string | null): string {
  if (value === null)
    return '';

  return t('dashboard.tts.defaultHint.value', { value });
}

function change(rules: TtsRulesChange): void {
  save.mutate(rules);
}

function commitCharacters(): void {
  const parsed = Math.trunc(Number(characters.value));
  const next = Number.isFinite(parsed)
    ? Math.min(mostCharacters.value ?? parsed, Math.max(FEWEST_CHARACTERS, parsed))
    : FEWEST_CHARACTERS;

  characters.value = String(next);

  if (next !== Number(stored.value?.maxCharacters)) {
    change({ maxCharacters: next });
  }
}

function pickStanding(value: AcceptableValue): void {
  if (typeof value === 'string') {
    change({ minPermission: value });
  }
}

const modeHint = computed(() => valueHint(standard.value && modeName(standard.value.mode)));
const providerHint = computed(() =>
  valueHint(standard.value && providerName(standard.value.defaultProvider)),
);
const standingHint = computed(() =>
  valueHint(standard.value && standingName(standard.value.minPermission)),
);
const charactersHint = computed(() =>
  valueHint(standard.value && characterCount(standard.value.maxCharacters)),
);
</script>

<template>
  <PanelCard
    data-view="tts-speaking"
    pad
    :title="$t('dashboard.tts.speaking.title')"
    :description="$t('dashboard.tts.speaking.description')"
    :state="state"
    :error-text="$t('dashboard.tts.settings.error')"
    @retry="config.refetch()"
  >
    <RowGroup v-if="stored">
      <SettingRow
        :model-value="stored.isEnabled"
        :disabled="!writable"
        :title="$t('dashboard.tts.speaking.enabled.title')"
        :description="$t('dashboard.tts.speaking.enabled.description')"
        :hint="switchHint(standard?.isEnabled)"
        @update:model-value="change({ isEnabled: $event })"
      />
      <ReadabilityRow
        :title="$t('dashboard.tts.speaking.mode.title')"
        :description="$t('dashboard.tts.speaking.mode.description')"
        :hint="modeHint"
      >
        <SegmentedControl
          :model-value="stored.mode"
          :label="$t('dashboard.tts.speaking.mode.title')"
          :options="modes"
          :disabled="!writable"
          @update:model-value="change({ mode: $event })"
        />
      </ReadabilityRow>
      <ReadabilityRow
        :title="$t('dashboard.tts.speaking.provider.title')"
        :description="$t('dashboard.tts.speaking.provider.description')"
        :hint="providerHint"
      >
        <SegmentedControl
          :model-value="stored.defaultProvider"
          :label="$t('dashboard.tts.speaking.provider.title')"
          :options="providers"
          :disabled="!writable"
          @update:model-value="change({ defaultProvider: $event })"
        />
      </ReadabilityRow>
      <ReadabilityRow
        :title="$t('dashboard.tts.speaking.standing.title')"
        :description="$t('dashboard.tts.speaking.standing.description')"
        :hint="standingHint"
      >
        <Select
          :model-value="stored.minPermission"
          :disabled="!writable"
          @update:model-value="pickStanding"
        >
          <SelectTrigger class="w-40" :aria-label="$t('dashboard.tts.speaking.standing.label')">
            <SelectValue />
          </SelectTrigger>
          <SelectContent position="popper" align="end">
            <SelectGroup>
              <SelectItem v-for="id in offered?.minPermissions" :key="id" :value="id">{{
                standingName(id)
              }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </ReadabilityRow>
      <ReadabilityRow
        :title="$t('dashboard.tts.speaking.maxCharacters.title')"
        :description="$t('dashboard.tts.speaking.maxCharacters.description')"
        :hint="charactersHint"
      >
        <Input
          v-model="characters"
          type="number"
          :min="FEWEST_CHARACTERS"
          :max="mostCharacters"
          class="w-24"
          :disabled="!writable"
          :aria-label="$t('dashboard.tts.speaking.maxCharacters.title')"
          @blur="commitCharacters"
          @keydown.enter="commitCharacters"
        />
      </ReadabilityRow>
      <SettingRow
        :model-value="stored.skipBotMessages"
        :disabled="!writable"
        :title="$t('dashboard.tts.speaking.skipBots.title')"
        :description="$t('dashboard.tts.speaking.skipBots.description')"
        :hint="switchHint(standard?.skipBotMessages)"
        @update:model-value="change({ skipBotMessages: $event })"
      />
    </RowGroup>

    <template v-if="writable" #footer>
      <TtsResetFooter />
    </template>
  </PanelCard>
</template>
