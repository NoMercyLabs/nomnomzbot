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
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useTtsConfig } from '@/api/tts';
import { useSetTtsRules, useTtsDefaults, useTtsOptions, type TtsRulesChange } from '@/api/ttsRules';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { FieldError } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useNumberField } from '@/composables/useNumberField';
import type { PanelState } from '@/composables/usePanelState';
import { useProviderName } from '@/composables/useProviderName';
import { useRefusalLines } from '@/composables/useRefusalLines';
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
const { refuse, clear, linesFor } = useRefusalLines('dashboard');
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

const mostCooldown = computed(() => {
  const limit = offered.value?.cooldownSecondsMax;

  return limit === undefined
    ? undefined
    : Number(limit);
});

const standings = computed(() =>
  (offered.value?.minPermissions ?? []).map((id) => ({ value: id, label: standingName(id) })),
);

const modes = computed(() =>
  (offered.value?.modes ?? []).map((id) => ({ value: id, label: modeName(id) })),
);

// The free bot voice runs on the keyless provider; my own key runs on the providers that need a key. A provider
// the other source cannot use is not offered, except the one already stored, so the row never hides what is set.
const KEYLESS_PROVIDERS = new Set<string>(['edge']);

const providers = computed(() => {
  const source = stored.value?.mode;
  const current = stored.value?.defaultProvider;

  return (offered.value?.providers ?? [])
    .filter((id) => {
      if (id === current || (source !== 'self_host' && source !== 'byok'))
        return true;

      return KEYLESS_PROVIDERS.has(id) === (source === 'self_host');
    })
    .map((id) => ({ value: id, label: providerName(id) }));
});

function standingName(id: string): string {
  return t(`dashboard.tts.speaking.standing.${id}`);
}

function modeName(id: string): string {
  return t(`dashboard.tts.speaking.mode.${id}`);
}

function characterCount(count: number | string): string {
  return t('dashboard.tts.speaking.maxCharacters.value', { count: String(count) });
}

function secondCount(count: number | string): string {
  return t('dashboard.tts.speaking.cooldown.value', { count: String(count) });
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

function saveChecked(rules: TtsRulesChange): void {
  clear();
  save.mutate(rules, { onError: refuse });
}

const characters = useNumberField({
  stored: () => Number(stored.value?.maxCharacters),
  fewest: FEWEST_CHARACTERS,
  most: () => mostCharacters.value,
  commit: (next) => saveChecked({ maxCharacters: next }),
});

const cooldown = useNumberField({
  stored: () => Number(stored.value?.cooldownSeconds),
  fewest: 0,
  most: () => mostCooldown.value,
  commit: (next) => saveChecked({ cooldownSeconds: next }),
});

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
const cooldownHint = computed(() =>
  valueHint(standard.value && secondCount(standard.value.cooldownSeconds)),
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
        <SegmentedControl
          :model-value="stored.minPermission"
          :label="$t('dashboard.tts.speaking.standing.label')"
          :options="standings"
          :disabled="!writable"
          @update:model-value="change({ minPermission: $event })"
        />
      </ReadabilityRow>
      <ReadabilityRow
        :title="$t('dashboard.tts.speaking.maxCharacters.title')"
        :description="$t('dashboard.tts.speaking.maxCharacters.description')"
        :hint="charactersHint"
      >
        <Input
          v-model="characters.text.value"
          type="number"
          :min="FEWEST_CHARACTERS"
          :max="mostCharacters"
          class="w-24"
          :disabled="!writable"
          :aria-label="$t('dashboard.tts.speaking.maxCharacters.title')"
          @blur="characters.save"
          @keydown.enter="characters.save"
        />
        <FieldError v-for="line in linesFor('maxCharacters')" :key="line" data-problem="maxCharacters">
          {{ line }}
        </FieldError>
      </ReadabilityRow>
      <ReadabilityRow
        :title="$t('dashboard.tts.speaking.cooldown.title')"
        :description="$t('dashboard.tts.speaking.cooldown.description')"
        :hint="cooldownHint"
      >
        <Input
          v-model="cooldown.text.value"
          type="number"
          min="0"
          :max="mostCooldown"
          class="w-24"
          :disabled="!writable"
          :aria-label="$t('dashboard.tts.speaking.cooldown.title')"
          @blur="cooldown.save"
          @keydown.enter="cooldown.save"
        />
        <FieldError v-for="line in linesFor('cooldownSeconds')" :key="line" data-problem="cooldownSeconds">
          {{ line }}
        </FieldError>
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
