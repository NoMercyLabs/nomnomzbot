<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's own provider keys (BYOK) and the spelling of the channel name. A key goes to the bot and is
// never shown again: the panel only says whether one is stored, and the box is emptied after a save.
import { computed, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useTtsConfig } from '@/api/tts';
import {
  byokProviders,
  useNamePronunciation,
  useTtsDefaults,
  useRemoveByokKey,
  useSetByokKey,
  useSetNamePronunciation,
  useSetTtsRules,
  type ByokProvider,
} from '@/api/ttsRules';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import PlaySampleButton from './PlaySampleButton.vue';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const config = useTtsConfig(channelId);
const defaults = useTtsDefaults(channelId);
const saveKey = useSetByokKey(channelId);
const removeKey = useRemoveByokKey(channelId);
const pronunciation = useNamePronunciation(channelId);
const savePronunciation = useSetNamePronunciation(channelId);

const saveRules = useSetTtsRules(channelId);
const { can } = useCan();
const writable = computed(() => can('tts:config:write'));
const keys = reactive<Record<ByokProvider, string>>({ azure: '', elevenlabs: '' });
const region = ref('');
const spelling = ref('');
const voiceId = computed(() => config.data.value?.defaultVoiceId ?? null);
const spokenName = computed(
  () => spelling.value.trim() || (pronunciation.data.value?.channelName ?? ''),
);

watch(
  () => pronunciation.data.value,
  (stored) => {
    spelling.value = stored?.pronunciation ?? '';
  },
  { immediate: true },
);

watch(
  () => config.data.value?.azureRegion,
  (stored) => {
    region.value = stored ?? '';
  },
  { immediate: true },
);

const stored = computed(
  () =>
    ({
      azure: config.data.value?.hasAzureByokKey ?? false,
      elevenlabs: config.data.value?.hasElevenLabsByokKey ?? false,
    }) as const satisfies Record<ByokProvider, boolean>,
);

// Like the rule rows, the region names the bot default under its box. When the bot returns none, no caption shows:
// "Default: none" would be false, since the bot falls back to its own region.
const regionDefault = computed<string | null>(() => {
  const standard = defaults.data.value?.azureRegion;

  return standard
    ? t('dashboard.tts.defaultHint.value', { value: standard })
    : null;
});

function commitRegion(): void {
  const next = region.value.trim();

  if (next !== (config.data.value?.azureRegion ?? '')) {
    saveRules.mutate({ azureRegion: next });
  }
}

function submitKey(provider: ByokProvider): void {
  saveKey.mutate(
    {
      provider,
      apiKey: keys[provider].trim(),
      region: provider === 'azure'
        ? region.value.trim()
        : undefined,
    },
    {
      onSuccess: () => {
        keys[provider] = '';
        toast.success(t('dashboard.tts.keys.saved'));
      },
      onError: () => toast.error(t('dashboard.tts.keys.saveFailed')),
    },
  );
}

function dropKey(provider: ByokProvider): void {
  removeKey.mutate(provider, { onError: () => toast.error(t('dashboard.tts.keys.removeFailed')) });
}

function submitSpelling(): void {
  savePronunciation.mutate(spelling.value.trim(), {
    onSuccess: () => toast.success(t('dashboard.tts.keys.spellingSaved')),
    onError: () => toast.error(t('dashboard.tts.keys.spellingFailed')),
  });
}
</script>

<template>
  <PanelCard
    data-view="tts-keys"
    pad
    :title="$t('dashboard.tts.keys.title')"
    :description="$t('dashboard.tts.keys.description')"
  >
    <div class="flex flex-col gap-4">
      <form
        v-for="provider in byokProviders"
        :key="provider"
        class="flex flex-col gap-3"
        :data-key-form="provider"
        @submit.prevent="submitKey(provider)"
      >
        <!-- The key and its own buttons share one row at every width; the Azure region sits under that row. -->
        <div class="flex items-end gap-2" data-key-row>
          <label class="flex min-w-0 flex-1 flex-col gap-1 text-sm font-medium">
            <span class="flex flex-wrap items-center gap-x-3 gap-y-1" data-key-label>
              {{ $t(`dashboard.tts.keys.provider.${provider}`) }}
              <StatusBadge
                :tone="stored[provider] ? 'ok' : 'off'"
                :label="
                  $t(
                    stored[provider]
                      ? 'dashboard.tts.keys.status.stored'
                      : 'dashboard.tts.keys.status.missing',
                  )
                "
              />
            </span>
            <Input
              v-model="keys[provider]"
              type="password"
              autocomplete="off"
              :placeholder="
                $t(stored[provider] ? 'dashboard.tts.keys.stored' : 'dashboard.tts.keys.none')
              "
            />
          </label>
          <div class="flex flex-none gap-2" data-key-actions>
            <Button
              v-if="stored[provider]"
              type="button"
              variant="ghost"
              @click="dropKey(provider)"
            >
              {{ $t("dashboard.tts.keys.remove") }}
            </Button>
            <Button
              type="submit"
              :disabled="keys[provider].trim() === '' || saveKey.isPending.value"
            >
              {{ $t("dashboard.tts.keys.save") }}
            </Button>
          </div>
        </div>
        <label
          v-if="provider === 'azure'"
          class="flex w-full flex-col gap-1 text-sm font-medium sm:w-44"
          data-key-region
        >
          {{ $t("dashboard.tts.keys.region") }}
          <Input
            v-model="region"
            autocomplete="off"
            :disabled="!writable"
            :placeholder="$t('dashboard.tts.keys.regionPlaceholder')"
            :aria-label="$t('dashboard.tts.keys.region')"
            @blur="commitRegion"
            @keydown.enter="commitRegion"
          />
          <span
            v-if="regionDefault"
            class="text-sm font-normal text-muted-foreground"
            data-region-default
          >
            {{ regionDefault }}
          </span>
        </label>
      </form>
      <form
        class="flex flex-col gap-2 border-t border-border pt-4"
        @submit.prevent="submitSpelling"
      >
        <label for="tts-name-spelling" class="text-sm font-medium">
          {{ $t("dashboard.tts.keys.spelling") }}
        </label>
        <div class="flex items-center gap-2" data-spelling-row>
          <Input
            id="tts-name-spelling"
            v-model="spelling"
            class="flex-1"
            maxlength="100"
            :placeholder="pronunciation.data.value?.channelName"
          />
          <PlaySampleButton
            v-if="voiceId"
            :voice-id="voiceId"
            :text="spokenName"
            :label="$t('dashboard.tts.keys.spellingPreview')"
          />
          <Button type="submit" :disabled="savePronunciation.isPending.value">
            {{ $t("dashboard.tts.keys.spellingSave") }}
          </Button>
        </div>
        <p class="text-sm text-muted-foreground" data-spelling-help>
          {{ $t("dashboard.tts.keys.spellingHelp") }}
        </p>
      </form>
    </div>
  </PanelCard>
</template>
