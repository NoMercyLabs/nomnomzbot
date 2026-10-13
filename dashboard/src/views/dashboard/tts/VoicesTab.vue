<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { refDebounced, useLocalStorage } from '@vueuse/core';
import Check from '~icons/nnz/check';
import WaveForm from '~icons/nnz/wave-form';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useSetTtsVoice,
  useTtsConfig,
  useTtsVoices,
  voiceFamily,
  voiceFamilyOrder,
  type TtsVoice,
  type TtsVoicePage,
} from '@/api/tts';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import VirtualList from '@/components/layout/VirtualList.vue';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useProviderName } from '@/composables/useProviderName';

import PlaySampleButton from './PlaySampleButton.vue';
import { groupVoices, localeLabel, type VoiceListItem } from './voiceGroups';

const ROW_HEIGHT = 56;
const SEARCH_DELAY = 250;
const ALL = 'all';
const SEPARATOR = ' · ';
const SHOWN_STYLES = 2;

const { t, te, locale } = useI18n();
const providerName = useProviderName();
const { channelId } = useCurrentChannel();
const config = useTtsConfig(channelId);
const search = ref('');
const family = useLocalStorage<string>('nnz.tts.voiceFamily', ALL);
const asked = refDebounced(search, SEARCH_DELAY);
const catalog = useTtsVoices(channelId, () => ({ q: asked.value }));
const pick = useSetTtsVoice(channelId);

const loaded = computed<TtsVoice[]>(() =>
  (catalog.data.value?.pages ?? []).flatMap((page: TtsVoicePage) => page.voices),
);

// The language chips filter the loaded pages; the server has no language-family filter.
const visible = computed<TtsVoice[]>(() =>
  family.value === ALL
    ? loaded.value
    : loaded.value.filter((voice: TtsVoice) => voiceFamily(voice.locale) === family.value),
);

const items = computed<VoiceListItem[]>(() => groupVoices(visible.value, locale.value));

type Chip = {
  value: string;
  label: string;
  count: number;
};

const chips = computed<Chip[]>(() =>
  [ALL, ...voiceFamilyOrder].map((value: string): Chip => ({
    value,
    label: t(`dashboard.tts.voices.family.${value}`),
    count:
      value === ALL
        ? loaded.value.length
        : loaded.value.filter((voice: TtsVoice) => voiceFamily(voice.locale) === value).length,
  })),
);

// The search box and the chips live inside the panel, so a new search must not swap the panel for a skeleton.
const settled = ref(false);

watch(
  () => catalog.data.value,
  (data: unknown) => {
    if (data !== undefined) {
      settled.value = true;
    }
  },
  { immediate: true },
);

const panelState = computed<PanelState>(() =>
  catalog.isError.value
    ? 'error'
    : settled.value
      ? 'ready'
      : 'loading',
);

const chosen = computed<string | null>(
  () => config.data.value?.defaultVoiceId ?? null,
);
const current = computed<TtsVoice | undefined>(() =>
  loaded.value.find((voice: TtsVoice) => voice.id === chosen.value),
);

// Only a total the bot returned is shown; the loaded pages never stand in for it.
const subtitle = computed<string | undefined>(() => {
  const total = catalog.data.value?.pages[0]?.total;

  return total === null || total === undefined
    ? undefined
    : t('dashboard.tts.voices.subtitle', { total });
});

// While the server has more pages, the loaded voices are not the whole catalog, so the count says "the first".
const count = computed<string>(() =>
  t(catalog.hasNextPage.value
    ? 'dashboard.tts.voices.countFirst'
    : 'dashboard.tts.voices.count', {
    shown: visible.value.length,
    loaded: loaded.value.length,
  }),
);

function genderLabel(gender: string): string {
  const key = `dashboard.tts.voices.gender.${gender.toLowerCase()}`;

  return te(key)
    ? t(key)
    : gender;
}

function detail(voice: TtsVoice): string {
  return [
    localeLabel(voice.locale, locale.value),
    genderLabel(voice.gender),
    voice.styles.slice(0, SHOWN_STYLES).join(', '),
  ]
    .filter((part: string) => part !== '')
    .join(SEPARATOR);
}

const currentName = computed<string | null>(() => current.value?.displayName ?? chosen.value);

const currentDetail = computed<string>(() =>
  [
    current.value
      ? `${localeLabel(current.value.locale, locale.value)}${SEPARATOR}${genderLabel(current.value.gender)}`
      : '',
    config.data.value
      ? providerName(config.data.value.defaultProvider)
      : '',
  ]
    .filter((part: string) => part !== '')
    .join(SEPARATOR),
);

function choose(voice: TtsVoice): void {
  if (voice.id !== chosen.value) {
    pick.mutate({ voiceId: voice.id, provider: voice.provider });
  }
}
</script>

<template>
  <BoardGrid :rows="2">
    <BoardCell :span="7" :rows="2">
      <PanelCard
        :title="$t('dashboard.tts.voices.title')"
        :description="subtitle"
        tier="lead"
        pad
        :state="panelState"
        :error-text="$t('dashboard.tts.voices.error')"
        @retry="catalog.refetch()"
      >
        <div class="flex h-full min-h-0 flex-col gap-3">
          <div
            v-if="chosen !== null"
            data-current-voice
            class="flex flex-none items-center gap-3 rounded-[12px] border border-border p-3"
          >
            <span
              class="flex size-10 flex-none items-center justify-center rounded-[8px] bg-muted text-muted-foreground"
            >
              <WaveForm class="size-5" aria-hidden="true" />
            </span>
            <span class="min-w-0 flex-1">
              <span class="block text-sm font-medium wrap-break-word">{{ currentName }}</span>
              <span class="block text-sm text-muted-foreground">{{ currentDetail }}</span>
              <span
                v-if="config.data.value?.followsPlatformDefaultVoice"
                class="block text-sm text-muted-foreground"
              >
                {{ $t('dashboard.tts.voices.current.platform') }}
              </span>
            </span>
            <PlaySampleButton :voice-id="chosen" />
          </div>
          <p v-else data-current-voice class="flex-none text-sm text-muted-foreground">
            {{ $t('dashboard.tts.voices.current.none') }}
          </p>

          <SearchField
            v-model="search"
            class="w-full flex-none"
            :placeholder="$t('dashboard.tts.voices.search')"
          />

          <div
            role="group"
            :aria-label="$t('dashboard.tts.voices.language')"
            class="flex flex-none flex-wrap gap-1.5"
          >
            <Button
              v-for="chip in chips"
              :key="chip.value"
              :data-voice-family="chip.value"
              type="button"
              variant="outline"
              size="sm"
              class="h-7 gap-1.5 px-2.5"
              :class="
                family === chip.value
                  ? 'bg-accent text-accent-foreground dark:bg-accent dark:hover:bg-accent'
                  : ''
              "
              :aria-pressed="family === chip.value"
              @click="family = chip.value"
            >
              {{ chip.label }}
              <span class="text-muted-foreground tabular-nums">{{ chip.count }}</span>
            </Button>
          </div>

          <div
            v-if="catalog.isPending.value"
            class="grid flex-none gap-2"
            :aria-label="$t('dashboard.tts.voices.loading')"
            aria-busy="true"
          >
            <Skeleton v-for="line in 3" :key="line" class="h-11 rounded-[12px]" />
          </div>
          <p
            v-else-if="visible.length === 0"
            class="py-6 text-center text-sm text-muted-foreground"
          >
            {{ $t('dashboard.tts.voices.empty') }}
          </p>
          <VirtualList
            v-else
            class="min-h-0 flex-1"
            :items="items"
            :item-key="(item: VoiceListItem) => item.key"
            :estimate-size="ROW_HEIGHT"
            :label="$t('dashboard.tts.voices.title')"
          >
            <template #default="{ item }">
              <div
                v-if="item.kind === 'heading'"
                data-voice-group
                class="px-3 pt-4 pb-1 text-xs font-medium text-muted-foreground"
              >
                {{ item.label }}
              </div>
              <SummaryRow
                v-else
                data-voice-row
                :data-voice-id="item.voice.id"
                pick
                role="radio"
                :aria-checked="item.voice.id === chosen"
                :selected="item.voice.id === chosen"
                :title="item.voice.displayName"
                :detail="detail(item.voice)"
                @click="choose(item.voice)"
              >
                <template #leading>
                  <Check
                    class="size-4 flex-none"
                    :class="item.voice.id === chosen ? '' : 'invisible'"
                    aria-hidden="true"
                  />
                </template>
                <template #trailing><PlaySampleButton :voice-id="item.voice.id" /></template>
              </SummaryRow>
            </template>
          </VirtualList>
        </div>
        <template #footer>
          <span data-voice-count class="text-sm text-muted-foreground">{{ count }}</span>
          <Button
            v-if="catalog.hasNextPage.value"
            data-voice-more
            variant="outline"
            size="sm"
            :disabled="catalog.isFetchingNextPage.value"
            @click="catalog.fetchNextPage()"
          >
            {{ $t('dashboard.tts.voices.more') }}
          </Button>
          <span class="ml-auto text-xs text-muted-foreground">{{
            $t('dashboard.tts.voices.hint')
          }}</span>
        </template>
      </PanelCard>
    </BoardCell>
    <BoardCell :span="5">
      <PanelCard :title="$t('dashboard.tts.voices.detection.title')" tier="quiet" pad>
        <p class="text-sm text-muted-foreground">{{ $t('dashboard.tts.voices.detection.body') }}</p>
      </PanelCard>
    </BoardCell>
  </BoardGrid>
</template>
