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

// Like the rule rows, the region names the bot default under its box; the bot has none until an operator sets one.
const regionDefault = computed<string>(() => {
  const standard = defaults.data.value?.azureRegion;

  return standard
    ? t('dashboard.tts.defaultHint.value', { value: standard })
    : t('dashboard.tts.defaultHint.none');
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
        class="flex flex-wrap items-end gap-2"
        :data-key-form="provider"
        @submit.prevent="submitKey(provider)"
      >
        <label class="flex min-w-40 flex-1 flex-col gap-1 text-sm font-medium">
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
        <label v-if="provider === 'azure'" class="flex w-44 flex-col gap-1 text-sm font-medium">
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
          <span v-if="defaults.data.value" class="text-sm font-normal text-muted-foreground" data-region-default>
            {{ regionDefault }}
          </span>
        </label>
        <!-- Both providers put the buttons in one group: on a phone it sits on its own line, from sm it stays inline. -->
        <div class="flex w-full justify-end gap-2 sm:w-auto" data-key-actions>
          <Button v-if="stored[provider]" type="button" variant="ghost" @click="dropKey(provider)">
            {{ $t("dashboard.tts.keys.remove") }}
          </Button>
          <Button type="submit" :disabled="keys[provider].trim() === '' || saveKey.isPending.value">
            {{ $t("dashboard.tts.keys.save") }}
          </Button>
        </div>
      </form>
      <form
        class="flex flex-wrap items-end gap-2 border-t border-border pt-4"
        @submit.prevent="submitSpelling"
      >
        <label class="flex min-w-40 flex-1 flex-col gap-1 text-sm font-medium">
          {{ $t("dashboard.tts.keys.spelling") }}
          <Input
            v-model="spelling"
            maxlength="100"
            :placeholder="pronunciation.data.value?.channelName"
          />
          <span class="text-sm font-normal text-muted-foreground">
            {{ $t("dashboard.tts.keys.spellingHelp") }}
          </span>
        </label>
        <Button type="submit" :disabled="savePronunciation.isPending.value">
          {{ $t("dashboard.tts.keys.spellingSave") }}
        </Button>
      </form>
    </div>
  </PanelCard>
</template>
