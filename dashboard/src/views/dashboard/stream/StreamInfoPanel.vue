<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The form behind "What viewers see". The page shows the server's values with the person's edits laid over them.
// A refused save keeps the edits, so nothing typed is lost.
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import {
  useSaveStreamInfo,
  useStreamHistory,
  useStreamInfo,
  type PastStream,
  type StreamInfo,
  type StreamInfoDraft,
} from '@/api/stream'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SettingRow from '@/components/layout/SettingRow.vue'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePanelState } from '@/composables/usePanelState'

import CategoryPicker from './CategoryPicker.vue'
import TagInput from './TagInput.vue'

// Twitch cuts a title at 140 characters.
const TITLE_MAX: number = 140
const RECENT_TITLES: number = 5
const CONTENT_LABELS: readonly string[] = [
  'mature',
  'language',
  'violence',
  'gambling',
  'drugs',
  'sexual',
  'promotion',
]

const { t, locale } = useI18n()
const { channelId } = useCurrentChannel()
const infoQuery = useStreamInfo(channelId)
const historyQuery = useStreamHistory(channelId)
const save = useSaveStreamInfo(channelId)

const draft: Ref<Partial<StreamInfoDraft>> = ref({})
const info: ComputedRef<StreamInfo | null> = computed(() => infoQuery.data.value ?? null)
const state = usePanelState(infoQuery, (data: StreamInfo | null) => data === null)

const form: ComputedRef<StreamInfoDraft> = computed(() => ({
  title: draft.value.title ?? info.value?.title ?? '',
  gameName: draft.value.gameName ?? info.value?.gameName ?? '',
  tags: draft.value.tags ?? [...(info.value?.tags ?? [])],
}))

const dirty: ComputedRef<boolean> = computed(
  () =>
    form.value.title !== (info.value?.title ?? '') ||
    form.value.gameName !== (info.value?.gameName ?? '') ||
    JSON.stringify(form.value.tags) !== JSON.stringify(info.value?.tags ?? []),
)

const history: ComputedRef<PastStream[]> = computed(() => historyQuery.data.value ?? [])

const recentTitles: ComputedRef<string[]> = computed(() => {
  const current = info.value?.title ?? ''
  const past = history.value
    .map((stream: PastStream) => stream.title ?? '')
    .filter((title: string) => title !== '' && title !== current)

  return [...new Set(past)].slice(0, RECENT_TITLES)
})

const recentCategories: ComputedRef<string[]> = computed(() => [
  ...new Set(
    history.value
      .map((stream: PastStream) => stream.gameName ?? '')
      .filter((name: string) => name !== ''),
  ),
])

const language: ComputedRef<string> = computed(() => {
  const code = info.value?.language ?? ''

  return new Intl.DisplayNames([locale.value], { type: 'language' }).of(code) ?? code
})

function setTitle(value: string | number): void {
  draft.value = { ...draft.value, title: String(value).slice(0, TITLE_MAX) }
}

function setGame(value: string): void {
  draft.value = { ...draft.value, gameName: value }
}

function setTags(value: string[]): void {
  draft.value = { ...draft.value, tags: value }
}

function submit(): void {
  save.mutate(form.value, {
    onSuccess: () => {
      draft.value = {}
    },
  })
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.stream.info.title')"
    :description="t('dashboard.stream.info.description')"
    tier="lead"
    :state="state"
    :empty-text="t('dashboard.stream.info.error')"
    :error-text="t('dashboard.stream.info.error')"
    @retry="infoQuery.refetch()"
  >
    <div class="flex flex-col gap-6">
      <div class="flex flex-col gap-2">
        <div class="flex items-baseline justify-between">
          <label for="stream-title" class="text-sm font-medium">{{
            t('dashboard.stream.info.titleLabel')
          }}</label>
          <span class="text-sm text-muted-foreground tabular-nums">
            {{ t('dashboard.stream.info.titleCount', { count: form.title.length }) }}
          </span>
        </div>
        <Textarea
          id="stream-title"
          :maxlength="TITLE_MAX"
          rows="2"
          :model-value="form.title"
          @update:model-value="setTitle"
        />
        <div v-if="info?.title || recentTitles.length > 0" class="flex flex-col gap-1">
          <span class="text-sm text-muted-foreground">{{
            t('dashboard.stream.info.recentTitles')
          }}</span>
          <RowGroup>
            <button
              v-if="info?.title"
              type="button"
              data-recent-title
              class="flex min-h-9 w-full flex-col rounded-[8px] px-3 py-1.5 text-left text-sm hover:bg-foreground/5"
              @click="setTitle(info.title)"
            >
              {{ info.title }}
              <span class="text-xs text-muted-foreground">{{
                t('dashboard.stream.info.currentTitle')
              }}</span>
            </button>
            <button
              v-for="title in recentTitles"
              :key="title"
              type="button"
              data-recent-title
              class="flex min-h-9 w-full items-center rounded-[8px] px-3 py-1.5 text-left text-sm hover:bg-foreground/5"
              @click="setTitle(title)"
            >
              {{ title }}
            </button>
          </RowGroup>
        </div>
      </div>

      <div class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ t('dashboard.stream.info.category') }}</span>
        <CategoryPicker
          :model-value="form.gameName"
          :channel-id="channelId"
          :recent="recentCategories"
          @update:model-value="setGame"
        />
      </div>

      <div class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ t('dashboard.stream.info.tags') }}</span>
        <TagInput :model-value="form.tags" @update:model-value="setTags" />
      </div>

      <div class="flex flex-col gap-1">
        <span class="text-sm font-medium">{{ t('dashboard.stream.info.language') }}</span>
        <span class="text-sm">{{ language }}</span>
        <span class="text-sm text-muted-foreground">{{
          t('dashboard.stream.info.languageHint')
        }}</span>
      </div>

      <div class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ t('dashboard.stream.info.labels') }}</span>
        <p class="text-sm text-muted-foreground">{{ t('dashboard.stream.info.labelsHint') }}</p>
        <RowGroup>
          <SettingRow
            v-for="label in CONTENT_LABELS"
            :key="label"
            :title="t(`dashboard.stream.info.label.${label}`)"
            disabled
          />
        </RowGroup>
      </div>

      <div class="flex items-center justify-end gap-2">
        <Button variant="ghost" :disabled="!dirty" @click="draft = {}">{{
          t('dashboard.stream.info.discard')
        }}</Button>
        <Button :disabled="!dirty || save.isPending.value" @click="submit">{{
          t('dashboard.stream.info.save')
        }}</Button>
      </div>
    </div>
  </PanelCard>
</template>
