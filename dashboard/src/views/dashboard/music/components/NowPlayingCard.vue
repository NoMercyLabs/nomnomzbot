<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useIntervalFn } from '@vueuse/core'
import { Music, Pause, Play, SkipForward } from '@lucide/vue'
import { computed, ref, type ComputedRef, type Ref } from 'vue'

import type { NowPlaying } from '@/api/music'
import { Button } from '@/components/ui/button'
import { Progress } from '@/components/ui/progress'

import { formatClock } from './formatClock'

const props = defineProps<{
  track: NowPlaying
  // The moment the bot last reported the position; the bar moves on from there while the song plays.
  observedAt: number
}>()

defineEmits<{ pause: []; resume: []; skip: [] }>()

const now: Ref<number> = ref(Date.now())
useIntervalFn(() => {
  now.value = Date.now()
}, 1000)

const durationMs: ComputedRef<number> = computed<number>(() => Number(props.track.durationMs))
const positionMs: ComputedRef<number> = computed<number>(() => {
  const reported: number = Number(props.track.progressMs)
  const elapsed: number = props.track.isPlaying ? Math.max(0, now.value - props.observedAt) : 0
  return Math.min(durationMs.value, reported + elapsed)
})
const percent: ComputedRef<number> = computed<number>(() =>
  durationMs.value > 0 ? (positionMs.value / durationMs.value) * 100 : 0,
)
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-4">
    <div class="flex min-h-0 items-center gap-4">
      <img v-if="track.imageUrl" :src="track.imageUrl" alt="" class="size-24 flex-none rounded-lg object-cover" />
      <div v-else class="flex size-24 flex-none items-center justify-center rounded-lg bg-muted text-muted-foreground">
        <Music class="size-8" aria-hidden="true" />
      </div>
      <div class="min-w-0 flex-1">
        <p class="text-lg font-semibold wrap-break-word">{{ track.trackName }}</p>
        <p class="text-sm text-muted-foreground">{{ track.artist }}</p>
        <p v-if="track.requestedBy" class="text-sm text-muted-foreground">
          {{ $t('dashboard.music.now.requestedBy', { name: track.requestedBy }) }}
        </p>
        <p class="text-sm text-muted-foreground">
          {{ $t('dashboard.music.now.volume', { percent: Number(track.volume) }) }}
        </p>
      </div>
    </div>
    <div class="grid gap-1">
      <Progress
        :model-value="percent"
        :aria-label="
          $t('dashboard.music.now.progress', { position: formatClock(positionMs), length: formatClock(durationMs) })
        "
      />
      <div class="flex justify-between text-sm text-muted-foreground">
        <span>{{ formatClock(positionMs) }}</span>
        <span>{{ formatClock(durationMs) }}</span>
      </div>
    </div>
    <div class="flex items-center gap-2">
      <Button v-if="track.isPlaying" :disabled="!track.canPause" @click="$emit('pause')">
        <Pause aria-hidden="true" />
        {{ $t('dashboard.music.now.pause') }}
      </Button>
      <Button v-else :disabled="!track.canResume" @click="$emit('resume')">
        <Play aria-hidden="true" />
        {{ $t('dashboard.music.now.play') }}
      </Button>
      <Button variant="outline" :disabled="!track.canSkipNext" @click="$emit('skip')">
        <SkipForward aria-hidden="true" />
        {{ $t('dashboard.music.now.skip') }}
      </Button>
    </div>
  </div>
</template>
