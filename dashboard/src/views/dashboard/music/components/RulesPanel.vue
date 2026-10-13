<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The request rules (mockup 15-music, panel 113-141): who may ask, one song per person, the blocked list.
// Edits stay in a draft until Save; Discard drops the draft without a request. Only changed fields are sent.
import { useTimeoutFn } from '@vueuse/core'
import { computed, ref, useId, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import {
  useBlockedTracks,
  useBlockTrack,
  useMusicConfig,
  useSaveMusicConfig,
  useUnblockTrack,
  type BlockedTrack,
  type BlockTrack,
  type MusicConfig,
  type MusicConfigChange,
} from '@/api/music'
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { DEFAULT_UNDO_MS } from '@/composables/useInlineFlow'
import { usePanelState } from '@/composables/usePanelState'

type Draft = { minTrustLevel: string; perPerson: number }

// The values the bot accepts for minTrustLevel (MusicService.ParseMinTrustLevel); anything else means everyone.
const trustLevels: readonly string[] = [
  'everyone',
  'subscribers',
  'vip',
  'moderators',
  'broadcaster',
]
const perPersonId = useId()

const { t } = useI18n()
const { channelId } = useCurrentChannel()
const configQuery = useMusicConfig(channelId)
const blockedQuery = useBlockedTracks(channelId)
const save = useSaveMusicConfig(channelId)
const unblock = useUnblockTrack(channelId)
const block = useBlockTrack(channelId)

const state = usePanelState(configQuery, (config: MusicConfig | null): boolean =>
  config === null)

function draftOf(config: MusicConfig): Draft {
  return { minTrustLevel: config.minTrustLevel, perPerson: Number(config.maxRequestsPerUser) }
}

const draft: Ref<Draft | null> = ref<Draft | null>(null)

watch(
  () => configQuery.data.value,
  (config: MusicConfig | null | undefined) => {
    draft.value = config
      ? draftOf(config)
      : null
  },
  { immediate: true },
)

const change: ComputedRef<MusicConfigChange> = computed<MusicConfigChange>(() => {
  const config = configQuery.data.value
  const edits = draft.value

  if (!config || !edits)
    return {}

  const result: MusicConfigChange = {}

  if (edits.minTrustLevel !== config.minTrustLevel) {
    result.minTrustLevel = edits.minTrustLevel
  }

  if (edits.perPerson !== draftOf(config).perPerson) {
    result.maxRequestsPerUser = edits.perPerson
  }

  return result
})
const valid = computed<boolean>(
  () =>
    draft.value === null || (Number.isInteger(draft.value.perPerson) && draft.value.perPerson >= 1),
)
const dirty = computed<boolean>(() => valid.value && Object.keys(change.value).length > 0)

function saveRules(): void {
  if (dirty.value) {
    save.mutate(change.value)
  }
}

function discard(): void {
  const config = configQuery.data.value

  draft.value = config
    ? draftOf(config)
    : null
}

const blocked: ComputedRef<BlockedTrack[]> = computed<BlockedTrack[]>(
  () => blockedQuery.data.value ?? [],
)

// Allow takes the row out at once; the notice below keeps the song for a short window so Undo can block it again.
const allowed: Ref<BlockedTrack | null> = ref<BlockedTrack | null>(null)
const undoWindow = useTimeoutFn(
  () => {
    allowed.value = null
  },
  DEFAULT_UNDO_MS,
  { immediate: false },
)

function allow(track: BlockedTrack): void {
  allowed.value = track
  unblock.mutate({ blockedTrackId: track.id })
  undoWindow.start()
}

function undoAllow(): void {
  const track = allowed.value

  if (track === null) return

  undoWindow.stop()
  allowed.value = null
  const body: BlockTrack = {
    provider: track.provider,
    trackUri: track.trackUri,
    title: track.title,
  }

  if (track.reason) {
    body.reason = track.reason
  }

  if (track.blockedByUserId) {
    body.blockedByUserId = track.blockedByUserId
  }

  block.mutate(body)
}

const missing = computed<KeyValueItem[]>(() =>
  ['cost', 'length', 'words'].map((key: string): KeyValueItem => ({
    label: t(`dashboard.music.rules.missing.${key}`),
    reason: t('dashboard.music.rules.notYet'),
  })),
)
</script>

<template>
  <PanelCard
    tier="quiet"
    pad
    :title="$t('dashboard.music.rules.title')"
    :state="state"
    :empty-text="$t('dashboard.music.rules.empty')"
    :error-text="$t('dashboard.music.rules.error')"
    @retry="configQuery.refetch()"
  >
    <div v-if="draft" class="grid gap-5">
      <section class="grid gap-2" :aria-label="$t('dashboard.music.rules.who')">
        <h3 class="text-sm font-medium">{{ $t('dashboard.music.rules.who') }}</h3>
        <RadioGroup v-model="draft.minTrustLevel" class="gap-2">
          <label v-for="level in trustLevels" :key="level" class="flex items-center gap-2 text-sm">
            <RadioGroupItem :value="level" />
            {{ $t(`dashboard.music.rules.levels.${level}`) }}
          </label>
        </RadioGroup>
      </section>
      <div class="flex items-center gap-3">
        <span class="min-w-0 flex-1">
          <label :for="perPersonId" class="block text-sm font-medium">{{
            $t('dashboard.music.rules.perPerson')
          }}</label>
          <span class="block text-sm text-muted-foreground">{{
            $t('dashboard.music.rules.perPersonHelp')
          }}</span>
        </span>
        <Input
          :id="perPersonId"
          v-model.number="draft.perPerson"
          name="songs-per-person"
          type="number"
          min="1"
          step="1"
          :aria-invalid="!valid || undefined"
          class="w-20"
        />
      </div>
      <section class="grid gap-2" :aria-label="$t('dashboard.music.rules.blocked')">
        <h3 class="text-sm font-medium">{{ $t('dashboard.music.rules.blocked') }}</h3>
        <p v-if="blockedQuery.isError.value" role="alert" class="text-sm text-destructive">
          {{ $t('dashboard.music.rules.blockedError') }}
        </p>
        <p
          v-else-if="!blockedQuery.isPending.value && blocked.length === 0"
          class="text-sm text-muted-foreground"
        >
          {{ $t('dashboard.music.rules.blockedEmpty') }}
        </p>
        <RowGroup v-if="blocked.length > 0">
          <SummaryRow
            v-for="track in blocked"
            :key="track.id"
            :title="track.title"
            :detail="track.reason ?? undefined"
            data-blocked-item
          >
            <template #trailing>
              <Button
                variant="outline"
                size="sm"
                :aria-label="$t('dashboard.music.rules.allowRow', { title: track.title })"
                @click="allow(track)"
              >
                {{ $t('dashboard.music.rules.allow') }}
              </Button>
            </template>
          </SummaryRow>
        </RowGroup>
        <p v-if="allowed" role="status" class="flex items-center gap-2 text-sm">
          <span class="min-w-0 flex-1">{{
            $t('dashboard.music.rules.allowed', { title: allowed.title })
          }}</span>
          <Button variant="outline" size="sm" @click="undoAllow">{{
            $t('common.flow.undo')
          }}</Button>
        </p>
      </section>
      <KeyValueList :items="missing" />
    </div>
    <template v-if="state === 'ready'" #footer>
      <Button variant="outline" size="sm" :disabled="!dirty" @click="discard">{{
        $t('dashboard.music.rules.discard')
      }}</Button>
      <Button size="sm" :disabled="!dirty" @click="saveRules">{{
        $t('dashboard.music.rules.save')
      }}</Button>
    </template>
  </PanelCard>
</template>
