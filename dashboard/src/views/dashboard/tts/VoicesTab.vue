<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import type { InfiniteData } from '@tanstack/vue-query'
import { refDebounced } from '@vueuse/core'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useSetTtsVoice, useTtsConfig, useTtsVoices, voiceFamily, type TtsConfig, type TtsVoice, type TtsVoicePage } from '@/api/tts'
import BoardCell from '@/components/layout/BoardCell.vue'
import BoardGrid from '@/components/layout/BoardGrid.vue'
import FilterBar, { type FilterOption } from '@/components/layout/FilterBar.vue'
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import SearchField from '@/components/layout/SearchField.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import VirtualList from '@/components/layout/VirtualList.vue'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePanelState } from '@/composables/usePanelState'
import { useProviderName } from '@/composables/useProviderName'

import PlaySampleButton from './PlaySampleButton.vue'

const ROW_HEIGHT = 56
const SEARCH_DELAY = 250
const ALL = 'all'
const families: readonly string[] = [ALL, 'en', 'nl', 'de', 'fr', 'es', 'other']

const { t } = useI18n()
const providerName = useProviderName()
const { channelId } = useCurrentChannel()
const config = useTtsConfig(channelId)
const search: Ref<string> = ref('')
const family: Ref<string> = ref(ALL)
const asked: Readonly<Ref<string>> = refDebounced(search, SEARCH_DELAY)
const catalogue = useTtsVoices(channelId, () => ({ q: asked.value }))
const pick = useSetTtsVoice(channelId)

const options: ComputedRef<FilterOption[]> = computed<FilterOption[]>(() =>
  families.map((value: string): FilterOption => ({ value, label: t(`dashboard.tts.voices.family.${value}`) })),
)

const loaded: ComputedRef<TtsVoice[]> = computed<TtsVoice[]>(() =>
  (catalogue.data.value?.pages ?? []).flatMap((page: TtsVoicePage) => page.voices),
)

// The language chips filter the loaded pages; the server has no language-family filter.
const visible: ComputedRef<TtsVoice[]> = computed<TtsVoice[]>(() =>
  family.value === ALL ? loaded.value : loaded.value.filter((voice: TtsVoice) => voiceFamily(voice.locale) === family.value),
)

const listState = usePanelState<InfiniteData<TtsVoicePage, number>>(catalogue, () => visible.value.length === 0)
const currentState = usePanelState<TtsConfig | null>(config, (data: TtsConfig | null) => data === null)

const chosen: ComputedRef<string | null> = computed<string | null>(() => config.data.value?.defaultVoiceId ?? null)
const current: ComputedRef<TtsVoice | undefined> = computed<TtsVoice | undefined>(() =>
  loaded.value.find((voice: TtsVoice) => voice.id === chosen.value),
)

const facts: ComputedRef<KeyValueItem[]> = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.tts.voices.fact.voice'), value: current.value?.displayName ?? chosen.value },
  { label: t('dashboard.tts.voices.fact.locale'), value: current.value?.locale },
  { label: t('dashboard.tts.voices.fact.gender'), value: current.value?.gender },
  {
    label: t('dashboard.tts.voices.fact.provider'),
    value: config.data.value ? providerName(config.data.value.defaultProvider) : undefined,
  },
])

// While the server has more pages, the loaded voices are not the whole catalogue, so the count says "the first".
const count: ComputedRef<string> = computed<string>(() =>
  t(catalogue.hasNextPage.value ? 'dashboard.tts.voices.countFirst' : 'dashboard.tts.voices.count', {
    shown: visible.value.length,
    loaded: loaded.value.length,
  }),
)

function detail(voice: TtsVoice): string {
  return [voice.locale, voice.gender, ...voice.styles.slice(0, 2)].filter((part: string) => part !== '').join(' · ')
}

function choose(voice: TtsVoice): void {
  if (voice.id !== chosen.value) pick.mutate({ voiceId: voice.id, provider: voice.provider })
}
</script>

<template>
  <BoardGrid :rows="2">
    <BoardCell :span="5">
      <PanelCard
        :title="$t('dashboard.tts.voices.current.title')"
        tier="lead"
        pad
        :state="currentState"
        :empty-text="$t('dashboard.tts.voices.current.none')"
        :error-text="$t('dashboard.tts.settings.error')"
        @retry="config.refetch()"
      >
        <div class="grid gap-4">
          <KeyValueList :items="facts" />
          <p v-if="config.data.value?.followsPlatformDefaultVoice" class="text-sm text-muted-foreground">
            {{ $t('dashboard.tts.voices.current.platform') }}
          </p>
        </div>
      </PanelCard>
    </BoardCell>
    <BoardCell :span="7" :rows="2">
      <PanelCard
        :title="$t('dashboard.tts.voices.title')"
        :state="listState"
        :empty-text="$t('dashboard.tts.voices.empty')"
        :error-text="$t('dashboard.tts.voices.error')"
        @retry="catalogue.refetch()"
      >
        <template #actions>
          <FilterBar v-model="family" :label="$t('dashboard.tts.voices.language')" :options="options">
            <SearchField v-model="search" :placeholder="$t('dashboard.tts.voices.search')" />
          </FilterBar>
        </template>
        <VirtualList
          class="h-full"
          :items="visible"
          :item-key="(voice: TtsVoice) => voice.id"
          :estimate-size="ROW_HEIGHT"
          :label="$t('dashboard.tts.voices.title')"
        >
          <template #default="{ item }">
            <SummaryRow
              data-voice-row
              :data-voice-id="item.id"
              pick
              role="radio"
              :aria-checked="item.id === chosen"
              :selected="item.id === chosen"
              :title="item.displayName"
              :detail="detail(item)"
              @click="choose(item)"
            >
              <template #trailing><PlaySampleButton :voice-id="item.id" /></template>
            </SummaryRow>
          </template>
        </VirtualList>
        <template #footer>
          <span data-voice-count class="mr-auto text-sm text-muted-foreground">{{ count }}</span>
          <Button
            v-if="catalogue.hasNextPage.value"
            data-voice-more
            variant="outline"
            size="sm"
            :disabled="catalogue.isFetchingNextPage.value"
            @click="catalogue.fetchNextPage()"
          >
            {{ $t('dashboard.tts.voices.more') }}
          </Button>
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
